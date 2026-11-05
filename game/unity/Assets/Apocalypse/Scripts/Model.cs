using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apocalypse
{
    public enum NodeType { Road, Shelter, Hospital, Supply, Factory, Generator, Armory }
    public enum TaskType { Delivery, Rescue, Part, Fuel }
    public enum TaskStatus { Announced, Open, Carried, Done, Expired }
    public enum PlayerState { Active, Downed, Eliminated }
    public enum MatchStatus { Lobby, Running, Ended }

    [System.Serializable] public class MapNodeData { public float x, y; public string type = "road"; public bool infected; }
    [System.Serializable] public class MapEdgeData { public int a, b; public bool bridge; }
    [System.Serializable] public class MapPoint { public float x, y; }
    [System.Serializable] public class MapData { public string name = ""; public MapNodeData[] nodes; public MapEdgeData[] edges; public MapPoint[] river; public string[] themes; }

    public class Node
    {
        public int id;
        public NodeType type;
        public Vector2 pos;
        public float infection;
        public int medkits, weapons;
        public bool powered;
        public int district;
        public List<int> edges = new List<int>();
        public bool Safe => type == NodeType.Shelter || type == NodeType.Hospital;
    }

    public class Edge
    {
        public int id, a, b;
        public float length;
        public bool bridge;
        public bool blocked;
        public float blockedUntil;
        public int Other(int n) => n == a ? b : a;
    }

    public class GameTask
    {
        public int id;
        public TaskType type;
        public int origin, dest, weight, reward, bonus;
        public bool urgent;
        public float deadline = -1f;
        public float expiresAt = -1f;
        public float availableAt;
        public TaskStatus status;
        public int carrier = -1;
        public int completedBy = -1;
        public int batch = -1;
    }

    public class Batch
    {
        public int id, origin, bonus;
        public float deadline;
        public int owner = -1, delivered;
        public bool paid, failed;
        public List<int> tasks = new List<int>();
        public Dictionary<int, int> by = new Dictionary<int, int>();
    }

    public class PlanStep
    {
        public string action;
        public int node = -1, task = -1, target = -1;
        public float seconds;
    }

    public class Team
    {
        public int id;
        public string name, token, botKind = "";
        public Color color;
        public int rank;
        public readonly List<Player> units = new List<Player>();
        public bool IsBot => botKind != "";
        public int Score => units.Sum(u => u.score);
        public int Alive => units.Count(u => u.state != PlayerState.Eliminated);
    }

    public class Player
    {
        public int id;
        public string name, token;
        public Color color;
        public IBot bot;
        public string botKind = "";
        public bool IsBot => bot != null;
        public Team team;
        public int unit;

        public int node, nextNode = -1;
        public float progress;
        public Vector2 pos;

        public float hp = 100f, infection;
        public List<int> cargo = new List<int>();
        public int medkits;

        public PlayerState state;
        public float downedTimer;
        public float eliminatedAt = -1f;
        public string eliminatedReason = "";

        public int score, delivered, rescued, revives, downs;
        public bool evacuated;
        public float cityReduced;
        public string role = "";
        public int parts;
        public bool escaped;
        public int fixedCount;
        public int fuel;
        public int combo;
        public float lastScoreAt = -999f;
        public float protectUntil = -1f;
        public float revivedAt = -999f;
        public int ammo, kills;
        public float fireCooldown;

        public List<PlanStep> plan = new List<PlanStep>();
        public int planIndex;
        public PlanStep pending;
        public float actionTimer;
        public string lastError = "";
        public float lastPlanTime = -999f;
        public float idleSince = -1f;
        public float noiseTimer, lastScream = -999f;
        public int rank;
    }

    public enum ZombieMode { Wander, Attracted, Chase }

    public class Incident
    {
        public int id, node, minPeople, reward, edge = -1, workers;
        public string kind, name, status = "active";
        public float need, progress, deadline;
        public readonly Dictionary<int, float> work = new Dictionary<int, float>();
    }

    public class Zombie
    {
        public int id;
        public int node, nextNode = -1;
        public float progress, wait;
        public Vector2 pos;
        public ZombieMode mode;
        public int target = -1;
        public int chasing = -1;
        public int fromPlayer = -1;
        public System.Random rng;
    }

    public class Noise
    {
        public int id, node;
        public Vector2 pos;
        public float radius, until;
        public string source;
    }

    public class GameEvent
    {
        public float time;
        public string type, text;
        public int node = -1;
    }
}
