// CombatWorker.cs
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json; // optional: use Unity's JsonUtility if you prefer

public class CombatWorker : MonoBehaviour
{
    TcpListener listener;
    Thread listenerThread;
    bool running = false;

    void Start()
    {
        Application.runInBackground = true;
        StartServer(5555);
    }

    void OnDestroy()
    {
        StopServer();
    }

    void StartServer(int port)
    {
        listener = new TcpListener(IPAddress.Parse("192.168.1.101"), port);
        listener.Start();
        running = true;
        listenerThread = new Thread(ListenLoop);
        listenerThread.IsBackground = true;
        listenerThread.Start();
        Debug.Log("CombatWorker listening on port " + port);
    }

    void StopServer()
    {
        running = false;
        listener?.Stop();
        try { listenerThread?.Join(500); } catch { }
    }

    void ListenLoop()
    {
        try
        {
            while (running)
            {
                if (!listener.Pending())
                {
                    Thread.Sleep(10);
                    continue;
                }
                TcpClient client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(HandleClient, client);
            }
        }
        catch (SocketException e)
        {
            Debug.Log("Listener stopped: " + e);
        }
    }

    void HandleClient(object obj)
    {
        var client = (TcpClient)obj;
        using (var stream = client.GetStream())
        {
            var buf = new byte[1024];
            var sb = new StringBuilder();

            try
            {
                while (client.Connected)
                {
                    int read = stream.Read(buf, 0, buf.Length);
                    if (read == 0) break; // client closed

                    sb.Append(Encoding.UTF8.GetString(buf, 0, read));

                    // Here we assume messages end with newline (\n)
                    string content = sb.ToString();
                    int newlineIndex;
                    while ((newlineIndex = content.IndexOf('\n')) >= 0)
                    {
                        string reqJson = content.Substring(0, newlineIndex).Trim();
                        content = content.Substring(newlineIndex + 1);

                        if (!string.IsNullOrEmpty(reqJson))
                        {
                            Debug.Log("CombatWorker Received: " + reqJson);

                            // Parse request
                            CombatRequest req = null;
                            try
                            {
                                req = JsonConvert.DeserializeObject<CombatRequest>(reqJson);
                            }
                            catch (Exception ex)
                            {
                                Debug.Log("JSON parse error: " + ex.Message);
                                continue;
                            }

                            // Simulate combat
                            CombatResult result = CombatSystem.SimulateOnce(req);

                            // Send response
                            string respJson = JsonConvert.SerializeObject(result) + "\n";
                            byte[] respBytes = Encoding.UTF8.GetBytes(respJson);
                            stream.Write(respBytes, 0, respBytes.Length);
                            stream.Flush();
                        }
                    }

                    sb.Clear();
                    sb.Append(content); // leftover
                }
            }
            catch (Exception e)
            {
                Debug.Log("Client error: " + e.Message);
            }
        }
        client.Close();
    }


    // Simple DTOs
    [Serializable]
    public class CombatRequest
    {
        public int match_id;
        public List<PlayerState> players;
        public List<PlayerAction> actions;
    }

    [Serializable]
    public class PlayerState { public int id; public float x; public float y; public int hp; }

    [Serializable]
    public class PlayerAction { public int id; public string action; public float dirx; public float diry; }

    [Serializable]
    public class CombatResult
    {
        public List<CombatEvent> events;
    }

    [Serializable]
    public class CombatEvent { public string type; public int attacker; public int target; public int damage; }

    // Very small combat logic for demo
    static class CombatSystem
    {
        public static CombatResult SimulateOnce(CombatRequest req)
        {
            var res = new CombatResult { events = new List<CombatEvent>() };
            // For each action ATTACK, if target within 1.5 units, apply 10 damage to nearest other player
            foreach (var act in req.actions)
            {
                if (act.action == "ATTACK")
                {
                    var attacker = req.players.Find(p => p.id == act.id);
                    if (attacker == null) continue;
                    // find nearest other
                    PlayerState nearest = null;
                    float bestd = float.MaxValue;
                    foreach (var p in req.players)
                    {
                        if (p.id == attacker.id) continue;
                        float dx = p.x - attacker.x;
                        float dy = p.y - attacker.y;
                        float d = dx * dx + dy * dy;
                        if (d < bestd) { bestd = d; nearest = p; }
                    }
                    if (nearest != null && bestd <= 1.5f * 1.5f)
                    {
                        var ev = new CombatEvent { type = "HIT", attacker = attacker.id, target = nearest.id, damage = 10 };
                        res.events.Add(ev);
                        // simple death event if hp <= damage
                        if (nearest.hp - 10 <= 0)
                            res.events.Add(new CombatEvent { type = "DEATH", attacker = attacker.id, target = nearest.id, damage = 0 });
                    }
                }
            }
            return res;
        }
    }
}
