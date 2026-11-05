from __future__ import annotations

import math
import time as _time

import numpy as np

INF = 1e9
F03, F05, F06, F07 = (float(np.float32(x)) for x in (0.3, 0.5, 0.6, 0.7))
PHASE_KEYS = ["early", "mid", "late", "final"]
PHASE_ENDS = [0.25, 0.5833, 0.8333, 1.0]
PALETTE = ["#4DC2F7", "#FF7042", "#BA68C7", "#FFD64F", "#66BA6B", "#F06191", "#4FD1C4", "#FA9E21", "#9EA8FF", "#CCDB38"]

DEFAULTS = dict(
    mapCols=10, mapRows=8, bridges=2,
    batchChance=0.6, batchMin=3, batchMax=5, batchBonusRatio=1.0, taskInterval=9.0, announceLead=30.0,
    seed=42, matchDuration=600.0, randomBots=0, greedyBots=0, botReactionTime=1.0,
    planCooldown=15.0, maxPlanSteps=200, capacity=10, playerSpeed=3.0, loadSpeedPenalty=0.05, fuelWeight=4, weaponWeight=4, weaponShots=8, weaponStock=2, killReward=5, weaponRange=3.5, weaponInterval=1.2,
    downedTime=15.0, survivalBonus=120, reviveBonus=60, respawnPenalty=50, respawnHp=70.0,
    cityRate=0.55, spreadAmount=0.15, spreadGrow=0.02,

    coop=True, coopCityRate=1.35, cityPerPlayer=0.02, cityPerPoint=0.03, classBonus=200, urgentFailCity=3.0, rescueFailCity=2.0,
    coopTaskPlayers=3.0,

    escape=True, partsBase=0.0, partsPerPlayer=7.0, partsCrowding=0.15, difficulty=1.2, earliestDepart=480.0, departDelay=60.0, escapeBonus=200, earlyBonusPerSec=1.0,
    zoneStart=90.0, zoneEnd=540.0, zoneMinRadius=7.0, zoneDamage=3.0, zoneInfection=1.0,

    incidentInterval=30.0, roadblockInterval=45.0, roadblockTime=60.0, civilianZombies=1, civiliansPerPlayer=2.5, trappedCivilians=4, generatorWeight=0.04, generatorCooldown=90.0, speedBoost=1.2, speedBoostTime=60.0, guardDownTime=60.0,
    guardRange=5.0, guardFireInterval=1.0,

    fuelPerPlayer=0.6, fuelSpare=4, fuelSparePerPlayer=0.3, fuelReward=80, boardTime=45.0,

    stunTime=40.0,

    partWeightReward=12.0, rescueRewardMul=2.0, incidentRewardMul=2.0,

    partDecay=80.0,
    comboWindow=25.0, comboStep=10, comboMax=5,
    zombieSpeed=1.4, zombieSpeedPerPhase=0.2, zombieSight=5.5, zombieAttackRange=0.9,
    zombieDamage=5.5, zombieInfection=2.5, zombieChaseMul=1.25, zombieCapBase=6.0, zombieCapGrowth=40.0, zombieSpawnMul=1.6,
    actionNoiseRadius=10.0, actionNoiseDuration=12.0, survivorNoiseRadius=8.0, screamRadius=12.0,
)

def _i32(x):
    x &= 0xFFFFFFFF
    return x - (1 << 32) if x >= 1 << 31 else x

class DotNetRandom:
    MBIG, MSEED = 2147483647, 161803398

    def __init__(self, seed: int):
        seed = _i32(seed)
        sub = self.MBIG if seed == -2147483648 else abs(seed)
        a = [0] * 56
        mj = _i32(self.MSEED - sub)
        a[55] = mj
        mk = 1
        for i in range(1, 55):
            ii = (21 * i) % 55
            a[ii] = mk
            mk = _i32(mj - mk)
            if mk < 0:
                mk += self.MBIG
            mj = a[ii]
        for _ in range(4):
            for i in range(1, 56):
                a[i] = _i32(a[i] - a[1 + (i + 30) % 55])
                if a[i] < 0:
                    a[i] = _i32(a[i] + self.MBIG)
        self.a, self.inext, self.inextp = a, 0, 21

    def _sample(self):
        i = self.inext + 1
        if i >= 56:
            i = 1
        j = self.inextp + 1
        if j >= 56:
            j = 1
        r = _i32(self.a[i] - self.a[j])
        if r == self.MBIG:
            r -= 1
        if r < 0:
            r = _i32(r + self.MBIG)
        self.a[i] = r
        self.inext, self.inextp = i, j
        return r

    def next(self, n=None):
        if n is None:
            return self._sample()
        return int(self._sample() * (1.0 / self.MBIG) * n)

    def next_double(self):
        return self._sample() * (1.0 / self.MBIG)

def _f32(x):
    return float(np.float32(x))

def _dist(a, b):
    dx, dy = np.float32(a[0]) - np.float32(b[0]), np.float32(a[1]) - np.float32(b[1])
    return float(np.float32(math.sqrt(float(dx * dx + dy * dy))))

def _order_by_random(items, rng):
    keys = [rng.next() for _ in items]
    return [items[i] for i in sorted(range(len(items)), key=lambda i: keys[i])]

def _round(x):
    return int(round(x))

class Node:
    __slots__ = ("id", "type", "pos", "infection", "medkits", "edges", "district", "powered", "weapons")

    def __init__(self, id, pos):
        self.id, self.type, self.pos, self.infection, self.medkits, self.edges = id, "road", pos, 0.0, 0, []
        self.weapons = 0
        self.powered = False
        self.district = "city"

    @property
    def safe(self):
        return self.type in ("shelter", "hospital")

class Edge:
    __slots__ = ("id", "a", "b", "length", "bridge", "blocked", "blocked_until")

    def __init__(self, id, a, b, length, bridge):
        self.id, self.a, self.b, self.length, self.bridge = id, a, b, length, bridge
        self.blocked, self.blocked_until = False, 0.0

    def other(self, n):
        return self.b if n == self.a else self.a

DIAG_DIV, REMOVE_DIV, KEEP_DEG = 14, 1, 2

def _sign(x):
    return (x > 0) - (x < 0)

MAP_TYPES = {"extraction": "shelter", "hospital": "hospital", "supply": "supply", "factory": "factory",
             "generator": "generator", "armory": "armory", "road": "road"}

class CityMap:
    spacing = 6.0

    def __init__(self, cols, rows, bridges):
        self.cols, self.rows = max(4, cols), max(3, rows)
        self.bridge_count = min(max(bridges, 1), self.rows)
        self.river: list[tuple[int, int]] = []
        self.river_set: set = set()
        self.cuts: set = set()
        self.side_a: list[bool] = []
        self.custom = False
        self.themes = None
        self.river_points: list = []
        self.size = (0.0, 0.0)
        self.warp_x: list = []
        self.warp_y: list = []
        self.warp_w = self.warp_h = 0
        self.nodes: list[Node] = []
        self.edges: list[Edge] = []
        self.start_node = 0

    @property
    def scale(self):
        return len(self.nodes) / 48.0

    def west(self, node):
        return self.side_a[node]

    def load_custom(self, data):
        f = np.float32
        self.custom = True
        raw = data["nodes"]
        minx, miny = min(f(n["x"]) for n in raw), min(f(n["y"]) for n in raw)
        self.start_node = 0
        for i, d in enumerate(raw):
            nd = Node(i, (_f32(f(d["x"]) - minx), _f32(f(d["y"]) - miny)))
            nd.type = MAP_TYPES.get(d.get("type", "road"), "road")
            if nd.type == "supply":
                nd.medkits = 3
            if d.get("type") == "extraction":
                self.start_node = i
            self.nodes.append(nd)
        self.size = (max(n.pos[0] for n in self.nodes), max(n.pos[1] for n in self.nodes))
        count = len(self.nodes)
        for e in data.get("edges") or []:
            a, b = int(e["a"]), int(e["b"])
            if a != b and 0 <= a < count and 0 <= b < count:
                self._add(a, b, bool(e.get("bridge", False)))
        self.river_points = [(_f32(f(p["x"]) - minx), _f32(f(p["y"]) - miny)) for p in data.get("river") or []]
        self.themes = data.get("themes")
        side = [False] * count
        side[0] = True
        queue = [0]
        while queue:
            u = queue.pop()
            for i in self.nodes[u].edges:
                e = self.edges[i]
                v = e.other(u)
                if e.bridge or side[v]:
                    continue
                side[v] = True
                queue.append(v)
        self.side_a = side
        seeds = [i for i, d in enumerate(raw) if d.get("infected")]
        if not seeds:
            start = self.nodes[self.start_node].pos
            seeds = [n.id for n in sorted(self.nodes, key=lambda n: (-_dist(n.pos, start), n.id))[:2]]
        for s in seeds:
            self.nodes[s].infection = 1.0
            for i in self.nodes[s].edges:
                o = self.nodes[self.edges[i].other(s)]
                if o.infection < 0.5:
                    o.infection = 0.5
        self.recompute()

    def warp(self, gx, gy):
        f = np.float32
        u, v = f(gx) / f(3), f(gy) / f(3)
        i = min(max(int(math.floor(u)), 0), self.warp_w - 2)
        j = min(max(int(math.floor(v)), 0), self.warp_h - 2)
        fu, fv = u - f(i), v - f(j)
        W = self.warp_w

        def lerp2(w):
            a = w[j * W + i] + (w[j * W + i + 1] - w[j * W + i]) * fu
            b = w[(j + 1) * W + i] + (w[(j + 1) * W + i + 1] - w[(j + 1) * W + i]) * fu
            return a + (b - a) * fv
        return lerp2(self.warp_x), lerp2(self.warp_y)

    def river_in_cell(self, r, c):
        return (c + 1, r + 1) in self.river_set

    @staticmethod
    def _key(a, b):
        return min(a, b) * 10000 + max(a, b)

    def _cut(self, a, b):
        return self._key(a, b) in self.cuts

    def find_edge(self, a, b):
        for e in self.nodes[a].edges:
            if self.edges[e].other(a) == b:
                return self.edges[e]
        return None

    def _entry(self, side, rng):
        cols, rows = self.cols, self.rows
        if side == 0:
            return (2 + rng.next(cols - 3), 0)
        if side == 1:
            return (cols, 2 + rng.next(rows - 3))
        if side == 2:
            return (2 + rng.next(cols - 3), rows)
        return (0, 2 + rng.next(rows - 3))

    def _inward(self, p):
        return (min(max(p[0], 1), self.cols - 1), min(max(p[1], 1), self.rows - 1))

    @staticmethod
    def _stair(path, to, rng):
        p = path[-1]
        while p != to:
            dx, dy = _sign(to[0] - p[0]), _sign(to[1] - p[1])
            if dx != 0 and dy != 0:
                if rng.next(2) == 0:
                    dy = 0
                else:
                    dx = 0
            p = (p[0] + dx, p[1] + dy)
            path.append(p)

    def _step_edge(self, p, q):
        cols = self.cols
        if p[1] == q[1]:
            x = min(p[0], q[0])
            return self._key((p[1] - 1) * cols + x, p[1] * cols + x)
        y = min(p[1], q[1])
        return self._key(y * cols + p[0] - 1, y * cols + p[0])

    def _split(self, path):
        cols, rows = self.cols, self.rows
        self.cuts = {self._step_edge(path[i], path[i + 1]) for i in range(len(path) - 1)}
        side = [False] * (rows * cols)
        side[0] = True
        queue = [0]
        while queue:
            u = queue.pop(0)
            r, c = u // cols, u % cols
            for v in (u - 1 if c > 0 else -1, u + 1 if c + 1 < cols else -1, u - cols if r > 0 else -1, u + cols if r + 1 < rows else -1):
                if v >= 0 and not side[v] and not self._cut(u, v):
                    side[v] = True
                    queue.append(v)
        self.side_a = side

    def _generate_river(self, rng):
        cols, rows = self.cols, self.rows
        n, best_gap = rows * cols, None
        best = [(cols // 2, y) for y in range(rows + 1)]
        for _ in range(80):
            s0 = rng.next(4)
            s1 = (s0 + 1 + rng.next(3)) % 4
            a = self._entry(s0, rng)
            b = self._entry(s1, rng)
            mid = (2 + rng.next(cols - 3), 2 + rng.next(rows - 3))
            path = [a, self._inward(a)]
            self._stair(path, mid, rng)
            self._stair(path, self._inward(b), rng)
            path.append(b)
            if len(set(path)) != len(path):
                continue
            self._split(path)
            gap = abs(sum(self.side_a) * 2 - n)
            if best_gap is None or gap < best_gap:
                best_gap, best = gap, path
            if gap * 10 <= n:
                break
        self._split(best)
        self.river = best
        self.river_set = set(best)

    def generate(self, rng):
        cols, rows = self.cols, self.rows
        f = np.float32
        self.warp_w, self.warp_h = cols // 3 + 2, rows // 3 + 2
        self.warp_x, self.warp_y = [], []
        for _ in range(self.warp_w * self.warp_h):
            self.warp_x.append(f(rng.next_double() - 0.5) * f(5))
            self.warp_y.append(f(rng.next_double() - 0.5) * f(5))
        sx, sy = f((cols - 1) * self.spacing), f((rows - 1) * self.spacing)
        for r in range(rows):
            for c in range(cols):
                jx = f(rng.next_double() - 0.5) * f(2.2)
                jy = f(rng.next_double() - 0.5) * f(2.2)
                wx, wy = self.warp(c, r)
                x = min(max(f(c * self.spacing) + jx + wx, f(-2.5)), sx + f(2.5))
                y = min(max(f(r * self.spacing) + jy + wy, f(-2.5)), sy + f(2.5))
                self.nodes.append(Node(r * cols + c, (_f32(x), _f32(y))))
        self._generate_river(rng)
        steps = [self._step_edge(self.river[i], self.river[i + 1]) for i in range(len(self.river) - 1)]
        third = max(1, len(steps) // 3)
        bridge_keys = {steps[rng.next(third)]}
        bridge_keys.add(steps[len(steps) - 1 - rng.next(third)])
        if self.bridge_count > 2:
            for k in _order_by_random([k for k in steps if k not in bridge_keys], rng)[:self.bridge_count - 2]:
                bridge_keys.add(k)
        for r in range(rows):
            for c in range(cols):
                i = r * cols + c
                if c + 1 < cols:
                    if not self._cut(i, i + 1):
                        self._add(i, i + 1, False)
                    elif self._key(i, i + 1) in bridge_keys:
                        self._add(i, i + 1, True)
                if r + 1 < rows:
                    if not self._cut(i, i + cols):
                        self._add(i, i + cols, False)
                    elif self._key(i, i + cols) in bridge_keys:
                        self._add(i, i + cols, True)
        for _ in range(cols * rows // DIAG_DIV):
            c, r = rng.next(cols - 1), rng.next(rows - 1)
            down = rng.next(2) == 0
            if self.river_in_cell(r, c):
                continue
            if down:
                self._add(r * cols + c, (r + 1) * cols + c + 1, False)
            else:
                self._add(r * cols + c + 1, (r + 1) * cols + c, False)
        cands = _order_by_random([e for e in self.edges if not e.bridge], rng)[:len(self.edges) // REMOVE_DIV]
        for e in cands:
            if len(self.nodes[e.a].edges) <= KEEP_DEG or len(self.nodes[e.b].edges) <= KEEP_DEG:
                continue
            e.blocked = True
            if not self._reachable():
                e.blocked = False
                continue
            self._remove(e)
        self._assign_types(rng)
        self.recompute()

    def _add(self, a, b, bridge):
        old = self.find_edge(a, b)
        if old:
            return old
        e = Edge(len(self.edges), a, b, _dist(self.nodes[a].pos, self.nodes[b].pos), bridge)
        self.edges.append(e)
        self.nodes[a].edges.append(e.id)
        self.nodes[b].edges.append(e.id)
        return e

    def _remove(self, e):
        self.edges.remove(e)
        for n in self.nodes:
            n.edges.clear()
        for i, ed in enumerate(self.edges):
            ed.id = i
            self.nodes[ed.a].edges.append(i)
            self.nodes[ed.b].edges.append(i)

    def _assign_types(self, rng):
        cols, rows = self.cols, self.rows
        for s in (( rows - 1) * cols, cols - 1):
            self.nodes[s].infection = 1.0
            for e in self.nodes[s].edges:
                self.nodes[self.edges[e].other(s)].infection = 0.5

        def free(n):
            return n.type == "road" and n.infection == 0
        left = _order_by_random([n for n in self.nodes if free(n) and self.west(n.id)], rng)
        right = _order_by_random([n for n in self.nodes if free(n) and not self.west(n.id)], rng)

        def take(pool, t, count):
            for _ in range(count):
                if not pool:
                    return
                pool.pop(0).type = t
        extra = max(0, _round((rows * cols / 48.0 - 1.0) * 2.0))
        take(left, "shelter", 2 + extra // 2); take(left, "hospital", 1); take(left, "supply", 2); take(left, "factory", 1 + (extra + 1) // 2); take(left, "generator", 1)
        take(right, "shelter", 1 + extra // 2); take(right, "hospital", 1); take(right, "supply", 1 + extra // 2); take(right, "factory", 2 + (extra + 1) // 2); take(right, "generator", 1)
        take(left if rng.next(2) == 0 else right, "armory", 1)
        for n in self.nodes:
            if n.type == "supply":
                n.medkits = 3
        cx, cy = (cols - 1) * self.spacing * 0.5, (rows - 1) * self.spacing * 0.5
        shelters = [n for n in self.nodes if n.type == "shelter"]
        self.start_node = min(shelters, key=lambda n: _dist(n.pos, (cx, cy))).id

    def _reachable(self):
        seen = [False] * len(self.nodes)
        seen[0] = True
        queue, count = [0], 1
        while queue:
            u = queue.pop()
            for i in self.nodes[u].edges:
                e = self.edges[i]
                if e.blocked:
                    continue
                v = e.other(u)
                if not seen[v]:
                    seen[v] = True
                    count += 1
                    queue.append(v)
        return count == len(self.nodes)

    def connected(self):
        return bool(np.all(self.dist[0] < INF))

    def recompute(self):
        self.dist, self.next = self._floyd()

    def _floyd(self):
        n = len(self.nodes)
        d = np.full((n, n), np.float32(INF), dtype=np.float32)
        nx = np.full((n, n), -1, dtype=np.int64)
        np.fill_diagonal(d, 0)
        nx[np.arange(n), np.arange(n)] = np.arange(n)
        for e in self.edges:
            if e.blocked:
                continue
            d[e.a, e.b] = d[e.b, e.a] = e.length
            nx[e.a, e.b], nx[e.b, e.a] = e.b, e.a
        for k in range(n):
            reach = d[:, k] < INF
            alt = d[:, k:k + 1] + d[k:k + 1, :]
            better = (alt < d) & reach[:, None]
            d = np.where(better, alt, d)
            nx = np.where(better, nx[:, k:k + 1], nx)
        return d, nx

    def path(self, a, b, nxt=None):
        nxt = self.next if nxt is None else nxt
        if nxt[a, b] < 0:
            return None
        out, u = [], a
        while u != b:
            u = int(nxt[u, b])
            out.append(u)
        return out

class Task:
    def __init__(self, **kw):
        self.bonus, self.urgent, self.deadline, self.expires_at = 0, False, -1.0, -1.0
        self.carrier, self.completed_by, self.batch = -1, -1, -1
        self.__dict__.update(kw)

class Batch:
    def __init__(self, id, origin, deadline):
        self.id, self.origin, self.deadline = id, origin, deadline
        self.bonus, self.owner, self.delivered, self.paid, self.failed, self.tasks = 0, -1, 0, False, False, []
        self.by = {}

class Player:
    def __init__(self, id, name, color, bot=None, bot_kind=""):
        self.id, self.name, self.color, self.bot, self.bot_kind = id, name, color, bot, bot_kind
        self.team = id
        self.node, self.next_node, self.progress, self.pos = 0, -1, 0.0, (0.0, 0.0)
        self.hp, self.infection, self.cargo, self.medkits = 100.0, 0.0, [], 0
        self.state, self.downed_timer, self.eliminated_at, self.eliminated_reason = "active", 0.0, -1.0, ""
        self.score = self.delivered = self.rescued = self.revives = self.downs = 0
        self.evacuated = False
        self.city_reduced = 0.0
        self.plan, self.plan_index, self.pending, self.action_timer = [], 0, None, 0.0
        self.last_error, self.last_plan_time, self.idle_since = "", -999.0, -1.0
        self.noise_timer, self.last_scream, self.rank = 0.0, -999.0, 0
        self.role = ""
        self.fixed = 0
        self.fuel = 0
        self.parts = 0
        self.escaped = False
        self.combo, self.last_score_at = 0, -999.0
        self.protect_until = -1.0
        self.revived_at = -999.0
        self.ammo, self.kills, self.fire_cooldown = 0, 0, 0.0

class Zombie:
    def __init__(self, id, node, pos, from_player, rng):
        self.id, self.node, self.pos, self.from_player, self.rng = id, node, pos, from_player, rng
        self.next_node, self.progress, self.wait = -1, 0.0, 1.0
        self.mode, self.target, self.chasing = "wander", -1, -1

class Noise:
    def __init__(self, id, node, pos, radius, until, source):
        self.id, self.node, self.pos, self.radius, self.until, self.source = id, node, pos, radius, until, source

def _lerp(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)

def _vd(a, b):
    return math.hypot(a[0] - b[0], a[1] - b[1])

def _step(action, node=-1, task=-1, target=-1, seconds=0.0):
    return {"action": action, "node": node, "task": task, "target": target, "seconds": seconds}

def _heal_plan(sim, p):
    if p.medkits > 0:
        return [_step("use_medkit")]
    hs = [n for n in sim.map.nodes if n.type == "hospital"]
    if not hs:
        return []
    h = min(hs, key=lambda n: sim.map.dist[p.node, n.id])
    return [_step("goto", node=h.id), _step("heal")]

def _deliver_plan(sim, p):
    out = []
    for t in sorted([sim.task_by_id(i) for i in p.cargo if sim.task_by_id(i)], key=lambda t: sim.map.dist[p.node, t.dest]):
        out += [_step("goto", node=t.dest), _step("dropoff", task=t.id)]
    return out

def _do_task(t):
    return [_step("goto", node=t.origin), _step("pickup", task=t.id), _step("goto", node=t.dest), _step("dropoff", task=t.id)]

class RandomBot:
    def __init__(self, seed):
        self.rng = DotNetRandom(_i32(seed * 7919 + 1))

    def think(self, sim, p):
        if p.hp < 30 or p.infection > 70:
            return _heal_plan(sim, p)
        if p.cargo:
            return _deliver_plan(sim, p)
        opn = [t for t in sim.tasks if t.status == "open" and sim.can_take(p, t) and sim.load(p) + t.weight <= sim.cfg["capacity"]]
        if not opn:
            return [_step("wait", seconds=2)]
        return _do_task(opn[self.rng.next(len(opn))])

class GreedyBot:
    def think(self, sim, p):
        m = sim.map
        if p.hp < 45 or p.infection > 55:
            return _heal_plan(sim, p)
        if p.cargo:
            return _deliver_plan(sim, p)
        steps, frm = [], p.node
        if p.medkits == 0:
            sups = [n for n in m.nodes if n.type == "supply" and n.medkits > 0]
            if sups:
                s = min(sups, key=lambda n: m.dist[p.node, n.id])
                if m.dist[p.node, s.id] < 10:
                    steps += [_step("goto", node=s.id), _step("take_medkit")]
                    frm = s.id
        best, best_v = None, 0.0
        load = sim.load(p) + (1 if steps else 0)
        for t in sim.tasks:
            if t.status != "open" or not sim.can_take(p, t) or load + t.weight > sim.cfg["capacity"]:
                continue
            travel = float(m.dist[frm, t.origin] + m.dist[t.origin, t.dest])
            if travel >= INF:
                continue
            eta = sim.time + travel / sim.cfg["playerSpeed"] + 2
            gain = t.reward + (t.bonus if t.urgent and eta <= t.deadline else 0)
            v = gain * (1 - 0.5 * m.nodes[t.origin].infection) / (travel + 5)
            if v > best_v:
                best, best_v = t, v
        if best is None:
            return steps + [_step("wait", seconds=2)]
        return steps + _do_task(best)

ACTIONS = {"move", "goto", "pickup", "dropoff", "take_medkit", "use_medkit", "heal", "revive", "wait", "fix", "take_weapon", "drop", "drop_medkit", "drop_weapon"}
EMERGENCY = {"move", "goto", "wait", "use_medkit", "heal", "drop", "drop_medkit", "drop_weapon"}
DROPS = {"drop", "drop_medkit", "drop_weapon"}
S_TASK, S_EVENT, S_SPREAD, S_ZOMBIE, S_BLOCK = 1, 2, 3, 4, 5

INCIDENTS = {
    "outbreak": ("感染爆發", 8.0, 1, 60.0, 60),
    "bridge": ("橋樑損毀", 10.0, 1, 9999.0, 50),
    "airdrop": ("物資空投", 4.0, 1, 45.0, 40),
    "trapped": ("受困人群", 20.0, 2, 75.0, 120),
    "guard": ("撤離點告急", 8.0, 1, 50.0, 60),
    "heli": ("直升機故障", 24.0, 2, 60.0, 80),
    "medevac": ("醫療隊抵達", 6.0, 1, 50.0, 80),
    "napalm": ("呼叫空襲", 8.0, 1, 50.0, 60),
    "decoy": ("聲波誘餌", 20.0, 2, 60.0, 90),
    "chest": ("神秘寶箱", 3.0, 1, 40.0, 150),
    "boss": ("殭屍王", 30.0, 3, 75.0, 200),

    "herbs": ("草藥採集", 6.0, 1, 50.0, 70),
    "gas": ("加油站", 5.0, 1, 60.0, 80),
    "rubble": ("瓦礫堆", 20.0, 2, 70.0, 100),
    "bell": ("教堂鐘聲", 8.0, 1, 50.0, 70),

    "generator": ("發電機故障", 15.0, 1, 9999.0, 60),
}

DISTRICTS = ["jungle", "city", "ruins", "town"]
DISTRICT_NAMES = {"jungle": "雨林", "city": "城市", "ruins": "廢墟", "town": "小城鎮"}
DISTRICT_EVENT = {"jungle": "herbs", "city": "gas", "ruins": "rubble", "town": "bell"}

INCIDENT_TEXT = {
    "outbreak": ("壓制了感染爆發!", "感染爆發失控了..."),
    "bridge": ("修好了斷掉的橋!", "橋沒修好..."),
    "airdrop": ("搶到了空投物資!", "空投物資被搶走了..."),
    "trapped": ("救出了受困的民眾!", "沒能救出受困的民眾..."),
    "guard": ("守住了撤離點!", "撤離點失守了!"),
    "heli": ("排除了直升機故障!", "直升機故障沒排除!"),
    "medevac": ("接應到醫療隊!", "沒接應到醫療隊..."),
    "napalm": ("呼叫的空襲命中了!", "空襲沒呼叫成功..."),
    "decoy": ("啟動了聲波誘餌!", "聲波誘餌沒啟動..."),
    "chest": ("打開了神秘寶箱!", "沒搶到神秘寶箱..."),
    "boss": ("打倒了殭屍王!", "殭屍王沒被打倒..."),
    "herbs": ("採到了草藥!", "草藥沒採集到..."),
    "gas": ("裝滿了油桶!", "加油站的油被搶光了..."),
    "rubble": ("挖開了瓦礫堆!", "瓦礫堆沒挖開!"),
    "bell": ("敲響了教堂的鐘!", "教堂的鐘沒敲響!"),
    "generator": ("修好了發電機!", "發電機沒修好"),
}

class Incident:
    def __init__(self, id, kind, node, time):
        name, need, min_people, limit, reward = INCIDENTS[kind]
        self.id, self.kind, self.name, self.node = id, kind, name, node
        self.need, self.min_people, self.deadline, self.reward = need, min_people, time + limit, reward
        self.progress, self.status, self.workers, self.edge = 0.0, "active", 0, -1
        self.work = {}

class Sim:

    def __init__(self, map_data=None, **cfg):
        self.cfg = dict(DEFAULTS, **cfg)
        c = self.cfg
        self.rng = DotNetRandom(c["seed"])
        self.map = CityMap(c["mapCols"], c["mapRows"], c["bridges"])
        if map_data:
            self.map.load_custom(map_data)
        else:
            self.map.generate(self.rng)
        self.status, self.time, self.city_infection, self.end_reason = "lobby", 0.0, 0.0, ""
        self.class_won = False

        m = self.map
        if c["escape"]:
            for n in m.nodes:
                if n.type == "shelter" and n.id != m.start_node:
                    n.type = "road"
        self.extraction = m.start_node
        drng = DotNetRandom(_i32(self.cfg["seed"] * 7919 + 17))
        if m.themes and len(m.themes) == 2 and all(t in DISTRICTS for t in m.themes):
            west, east = m.themes
        else:
            west, east = DISTRICTS[drng.next(4)], DISTRICTS[drng.next(4)]
        for nd in m.nodes:
            nd.district = west if m.west(nd.id) else east
        for nd in m.nodes:
            if nd.type == "armory":
                nd.weapons = c["weaponStock"]
        self.player_shots = []
        self.parts, self.parts_required, self.ready_at, self.depart_at = 0, 0, -1.0, -1.0
        self.guard_kills = 0
        self.guard_cooldown, self.guard_shots = 0.0, []
        self.incidents: list[Incident] = []
        self.evacuated = 0
        self.speed_boost_until = self.guard_down_until = -1.0
        self.zombie_stun_until = -1.0
        self.medevac_timer, self.medevac_next, self.medevac_rolls = 5.0, 0.0, 0
        self.heli_boost = False
        self.fuel, self.fuel_required, self.fuel_discount = 0, 0, 0
        self.incident_timer = 25.0
        self.roadblock_timer, self.roadblock_index = 40.0, 0
        self.last_phase = 0
        self.last_generator_break = -999.0
        xs = [n.pos[0] for n in m.nodes]; ys = [n.pos[1] for n in m.nodes]
        ex = m.nodes[self.extraction].pos
        self.zone_max = max(math.hypot(x - ex[0], y - ex[1]) for x, y in zip(xs, ys)) + 1.0
        self.players: list[Player] = []
        self.tasks: list[Task] = []
        self.zombies: list[Zombie] = []
        self.events: list[dict] = []
        self.noises: list[Noise] = []
        self.batches: dict[int, Batch] = {}
        self._ids = dict(task=0, zombie=0, player=0, batch=0, noise=0, incident=0)
        self.task_timer = self.spread_timer = self.zombie_timer = self.restock_timer = self.bot_timer = 0.0
        self.event_timer = 20.0
        self.task_slot = self.event_index = self.spread_tick = self.zombie_spawn_index = 0

    def _id(self, k):
        v = self._ids[k]
        self._ids[k] += 1
        return v

    def use_stream(self, stream, index):
        c = self.cfg
        self.rng = DotNetRandom(_i32(_i32(c["seed"] * 73856093) ^ _i32(stream * 19349663) ^ _i32((index + 1) * 83492791)))

    def time_left(self):
        c = self.cfg
        return max(0.0, c["matchDuration"] - (self.depart_at - c["boardTime"] if c["escape"] and self.depart_at >= 0 else self.time))

    @property
    def phase(self):
        f = self.time / self.cfg["matchDuration"]
        for i, e in enumerate(PHASE_ENDS):
            if f < e:
                return i
        return 3

    @property
    def task_scale(self):
        if not self.cfg["coop"]:
            return 1.0
        return max(1.0, len(self.players) / self.cfg["coopTaskPlayers"])

    @property
    def zombie_speed(self):
        return self.cfg["zombieSpeed"] + self.cfg["zombieSpeedPerPhase"] * self.phase

    def log(self, type_, text, node=-1):
        self.events.append({"time": self.time, "type": type_, "text": text, "node": node})
        if len(self.events) > 200:
            self.events.pop(0)

    def add_player(self, name, bot=None, bot_kind=""):
        pid = self._id("player")
        p = Player(pid, name, PALETTE[pid % len(PALETTE)], bot, bot_kind)
        p.node = self.map.start_node
        p.pos = self.map.nodes[p.node].pos
        self.players.append(p)
        if self.cfg["escape"] and self.status == "running":
            self.parts_required = max(self.parts_required, self.parts_needed(len(self.players)))
        return p

    def parts_needed(self, n):
        c = self.cfg
        n = min(n, 30)
        return max(5, _round((c["partsBase"] + c["partsPerPlayer"] * n - c["partsCrowding"] * n * n) * c["difficulty"] * self.dscale))

    @property
    def dscale(self):
        return self.cfg["matchDuration"] / 600.0

    def start(self):
        c = self.cfg
        for i in range(c["randomBots"]):
            self.add_player(f"Bot-Random-{i + 1}", RandomBot(c["seed"] + i * 31), "random")
        for i in range(c["greedyBots"]):
            self.add_player(f"Bot-Greedy-{i + 1}", GreedyBot(), "greedy")
        self.status = "running"
        if c["escape"]:
            self.parts_required = self.parts_needed(len(self.players))
            gens = self.generators()
            for g in gens:
                self._break_generator(g)
            self._spawn_civilians()
            self.log("incident", "發電機都沒電！先修好發電機，工廠才會生產零件", gens[0].id if gens else -1)
        for _ in range(_round(6 * self.map.scale * self.task_scale)):
            self.use_stream(S_TASK, self.task_slot); self.task_slot += 1
            self.spawn_task(False, 0.0)
        for _ in range(_round(2 * self.map.scale)):
            self.use_stream(S_ZOMBIE, self.zombie_spawn_index); self.zombie_spawn_index += 1
            self.spawn_zombie_at_infected()

    def tick(self, dt):
        if self.status != "running":
            return
        self.time += dt
        self._roads(); self._infection(dt); self._tasks(dt); self._batches(); self._zombie_spawns(dt)
        if self.cfg["escape"]:
            self._check_medevac(dt)
            self._update_generators()
            self._spawn_incidents(dt)
            self._roadblocks(dt)
        else:
            self._events(dt)
        self._restock(dt); self._bots(dt)
        for p in self.players:
            self._update_player(p, dt)
        if self.cfg["escape"]:
            self._update_incidents(dt)
        self._noises(dt)
        if self.time >= self.zombie_stun_until:
            for z in self.zombies:
                self._update_zombie(z, dt)
        if self.cfg["escape"] and self.time >= self.guard_down_until:
            ex = self.extraction
            n0 = len(self.zombies)
            self.zombies = [z for z in self.zombies if not (z.node == ex and z.next_node < 0)]
            if len(self.zombies) < n0:
                self.guard_kills += n0 - len(self.zombies)

            self.guard_cooldown -= dt
            if self.guard_cooldown <= 0:
                expos, best, bd = self.map.nodes[ex].pos, None, self.cfg["guardRange"]
                for z in self.zombies:
                    d = _vd(z.pos, expos)
                    if d <= bd:
                        best, bd = z, d
                if best is not None:
                    self.zombies.remove(best)
                    self.guard_kills += 1
                    self.guard_cooldown = self.cfg["guardFireInterval"]
                    self.guard_shots = (self.guard_shots + [(self.time, best.pos)])[-20:]
        self._weapons(dt)
        self._damage(dt)
        if self.cfg["escape"]:
            self._helicopter()
        elif self.city_infection >= 100:
            self.end("城市感染程度達到上限，提前淪陷")
        elif self.time >= self.cfg["matchDuration"]:
            self.end("時間到，城市全面淪陷")
        elif self.players and all(p.state == "eliminated" for p in self.players):
            self.end("所有傭兵皆已淘汰")

    def _helicopter(self):
        c = self.cfg
        if self.ready_at < 0 and self.parts >= self.parts_required:
            self.ready_at = self.time
            for p in self.players:
                if p.state == "active":
                    p.hp = 100.0
                    p.infection = min(p.infection, 30.0)
            before = len(self.zombies)
            self.zombies = [z for z in self.zombies if not self.in_zone(z.pos)]
            self._spawn_fuel()
            self.log("heli", f"直升機修好了！守軍清掉圈內 {before - len(self.zombies)} 隻殭屍，去撿回 {self.fuel_required} 桶油！", self.extraction)
        if self.ready_at >= 0 and self.depart_at < 0 and self.fuel >= self.fuel_required:
            self.depart_at = self.time + c["boardTime"]
            self.log("heli", f"油加滿了！倒數暫停，{c['boardTime']:.0f} 秒後起飛，快回撤離點！", self.extraction)
        if self.depart_at >= 0 and self.time >= self.depart_at:
            self.end("直升機起飛")
        elif self.depart_at < 0 and self.time >= c["matchDuration"]:
            self.end("時間到，直升機沒修好" if self.ready_at < 0 else "時間到，油不夠，直升機飛不起來")
        elif self.players and all(p.state == "eliminated" for p in self.players):
            self.end("所有人都倒下了")

    @property
    def no_respawn(self):
        return self.cfg["escape"] and self.ready_at >= 0

    def doomed(self, p):
        return self.cfg["escape"] and not self.in_zone(p.pos)

    def _spawn_fuel(self):
        c, m = self.cfg, self.map
        self.use_stream(S_EVENT, 100000)
        n = max(1, len(self.players))
        self.fuel_required = max(2, _round(n * c["fuelPerPlayer"]) - self.fuel_discount)

        def ring(nd):
            if m.custom:
                f = np.float32
                x, y, sx, sy = f(nd.pos[0]), f(nd.pos[1]), f(m.size[0]), f(m.size[1])
                return int(min(min(x, sx - x), min(y, sy - y)) / f(m.spacing))
            r, cc = nd.id // m.cols, nd.id % m.cols
            return min(r, m.rows - 1 - r, cc, m.cols - 1 - cc)
        pool = [nd for nd in m.nodes if nd.type == "road" and m.dist[self.extraction, nd.id] >= 6 and self.in_zone(nd.pos)]
        if not pool:
            pool = [nd for nd in m.nodes if nd.type == "road" and m.dist[self.extraction, nd.id] >= 6] or \
                   [nd for nd in m.nodes if nd.type == "road"]
        edge = min(ring(nd) for nd in pool)
        far = [nd for nd in pool if ring(nd) <= edge]
        if len(far) < 4:
            far = [nd for nd in pool if ring(nd) <= edge + 1]
        reward = _round(c["fuelReward"] * (1 + 0.25 * self.phase))
        for _ in range(self.fuel_required + c["fuelSpare"] + int(n * c["fuelSparePerPlayer"])):
            node = far[self.rng.next(len(far))].id
            self.tasks.append(Task(id=self._id("task"), type="fuel", origin=node, dest=self.extraction, weight=int(c["fuelWeight"]),
                                   reward=reward, available_at=self.time, expires_at=-1.0, status="open"))

    @property
    def zone_radius(self):
        c = self.cfg
        t = self.time if self.ready_at < 0 else min(self.time, self.ready_at)
        z0, z1 = c["zoneStart"] * self.dscale, c["zoneEnd"] * self.dscale
        if not c["escape"] or t <= z0:
            return self.zone_max
        f = min(1.0, (t - z0) / max(1.0, z1 - z0))
        return self.zone_max + (c["zoneMinRadius"] - self.zone_max) * f

    def is_safe(self, node):
        n = self.map.nodes[node]
        if self.cfg["escape"] and node == self.extraction and self.time < self.guard_down_until:
            return False
        return n.safe

    def in_zone(self, pos):
        ex = self.map.nodes[self.extraction].pos
        return math.hypot(pos[0] - ex[0], pos[1] - ex[1]) <= self.zone_radius

    def _roads(self):
        changed = False
        for e in self.map.edges:
            if e.blocked and self.time >= e.blocked_until:
                e.blocked = False
                changed = True
        if changed:
            self.map.recompute()

    def _infection(self, dt):
        m = self.map
        avg = sum(n.infection for n in m.nodes) / len(m.nodes)
        c = self.cfg
        rate = c["coopCityRate"] + c["cityPerPlayer"] * len(self.players) if c["coop"] and not c["escape"] else c["cityRate"]
        self.city_infection += (100 / self.cfg["matchDuration"]) * rate * (0.6 + avg) * dt
        if c["escape"]:
            self.city_infection = min(100.0, self.city_infection)
        self.spread_timer -= dt
        if self.spread_timer > 0:
            return
        self.spread_timer = 4.0
        self.use_stream(S_SPREAD, self.spread_tick); self.spread_tick += 1
        chance = 0.10 + 0.35 * self.city_infection / 100
        add = [0.0] * len(m.nodes)
        for n in m.nodes:
            if n.infection < F06:
                continue
            for ei in n.edges:
                e = m.edges[ei]
                if not e.blocked and self.rng.next_double() < chance:
                    add[e.other(n.id)] += self.cfg["spreadAmount"]
        f = np.float32
        for n in m.nodes:
            cap = f(0.4) if n.safe else f(1.0)
            n.infection = float(min(cap, f(f(n.infection) + f(add[n.id])) + (f(self.cfg["spreadGrow"]) if n.infection > 0 else f(0))))

    def _tasks(self, dt):
        for t in self.tasks:
            if t.status == "announced" and self.time >= t.available_at:
                t.status = "open"
            if t.status == "open" and t.expires_at > 0 and self.time >= t.expires_at:
                t.status = "expired"
                if self.cfg["escape"]:
                    if t.type == "rescue":
                        for _ in range(self.cfg["civilianZombies"]):
                            self.spawn_zombie(t.origin, -1)
                        self.log("fail", f"倖存者 #{t.id} 沒人救，變成了殭屍", t.origin)
                elif self.cfg["coop"]:
                    if t.urgent:
                        self.city_infection += self.cfg["urgentFailCity"]
                        self.log("fail", f"緊急委託 #{t.id} 沒人處理，城市感染 +{self.cfg['urgentFailCity']:.0f}%", t.origin)
                    elif t.type == "rescue":
                        self.city_infection += self.cfg["rescueFailCity"]
                elif t.type == "rescue":
                    self.city_infection += 1
        self.tasks = [t for t in self.tasks if not (t.status in ("done", "expired") and self.time - t.expires_at > 30 and t.carrier < 0)]
        self.task_timer -= dt
        if self.task_timer > 0 or (self.cfg["escape"] and self.ready_at >= 0):
            return
        self.task_timer = self.cfg["taskInterval"] / (self.map.scale * self.task_scale * (1 + 0.35 * self.phase))
        self.use_stream(S_TASK, self.task_slot); self.task_slot += 1
        self.spawn_task(self.rng.next_double() < 0.15 + 0.07 * self.phase, self.cfg["announceLead"])

    def spawn_task(self, urgent, lead):
        m, rng, c, ph = self.map, self.rng, self.cfg, self.phase
        available_at = self.time + lead
        rescue = not c["escape"] and rng.next_double() < 0.25 + 0.1 * ph
        if rescue:
            pool = [n for n in m.nodes if n.type == "road" and n.infection >= F03] or [n for n in m.nodes if n.type == "road"]
            origin = pool[rng.next(len(pool))].id
            if c["escape"]:
                dest = self.extraction
            else:
                safe = [n for n in m.nodes if n.safe]
                dest = safe[rng.next(len(safe))].id
            weight = 4
        else:

            src = [n for n in m.nodes if n.type == "factory" and self.powered(n)] if c["escape"] else \
                  [n for n in m.nodes if n.type in ("factory", "supply")]
            if not src:
                return None
            origin = src[rng.next(len(src))].id
            if c["escape"]:
                dest, weight = self.extraction, 1 + rng.next(3)
            elif not urgent and rng.next_double() < c["batchChance"]:
                first = self._spawn_batch(origin, available_at)
                if first is not None:
                    return first
            if not c["escape"]:
                dst = [n for n in m.nodes if n.id != origin and n.type != "factory" and m.dist[origin, n.id] >= 8]
                if not dst:
                    return None
                dest = dst[rng.next(len(dst))].id
                weight = 1 + rng.next(4)
        d = float(m.dist[origin, dest])
        if d >= INF:
            return None
        if rescue:
            base = (18 + d * 0.5 + 20 * m.nodes[origin].infection) * c["rescueRewardMul"]
        elif c["escape"]:
            base = 10 + d * 0.6 + weight * c["partWeightReward"]
        else:
            base = 8 + d * 0.5 + weight * 2
        base *= 1 + 0.25 * ph
        kind = "rescue" if rescue else ("part" if c["escape"] else "delivery")
        t = Task(id=self._id("task"), type=kind, origin=origin, dest=dest, weight=weight,
                 reward=_round(base), available_at=available_at, expires_at=available_at + 90,
                 status="announced" if lead > 0 else "open")
        if urgent:
            t.urgent = True
            t.bonus = _round(base * 0.6)
            t.deadline = available_at + (d + 15) / c["playerSpeed"] * 1.5 + 25
        self.tasks.append(t)
        return t

    def _spawn_batch(self, origin, available_at):
        m, rng, c = self.map, self.rng, self.cfg
        centers = [n for n in m.nodes if n.id != origin and 12 <= m.dist[origin, n.id] < INF]
        if not centers:
            return None
        center = centers[rng.next(len(centers))]
        area = _order_by_random([n for n in m.nodes if n.id != origin and n.type != "factory" and m.dist[center.id, n.id] <= 9], rng)
        k = min(len(area), c["batchMin"] + rng.next(c["batchMax"] - c["batchMin"] + 1))
        if k < 2:
            return None
        dests = [n.id for n in area[:k]]
        tour, at, left = 0.0, origin, list(dests)
        while left:
            nxt = min(left, key=lambda n: m.dist[at, n])
            tour += float(m.dist[at, nxt]); at = nxt; left.remove(nxt)
        b = Batch(self._id("batch"), origin, available_at + 25 + 1.4 * tour / c["playerSpeed"] + 2 * k)
        first, total = None, 0
        for dest in dests:
            w = 1 + rng.next(2)
            d = float(m.dist[origin, dest])
            t = Task(id=self._id("task"), type="delivery", origin=origin, dest=dest, weight=w,
                     reward=_round((6 + d * 0.45 + w * 2) * (1 + 0.25 * self.phase)),
                     available_at=available_at, expires_at=available_at + 120,
                     status="announced" if available_at > self.time else "open", batch=b.id, deadline=b.deadline)
            self.tasks.append(t)
            b.tasks.append(t.id)
            total += t.reward
            first = first or t
        b.bonus = _round(total * c["batchBonusRatio"])
        self.batches[b.id] = b
        return first

    def batch_of(self, t):
        return self.batches.get(t.batch) if t is not None and t.batch >= 0 else None

    def can_take(self, p, t):
        if self.cfg["coop"]:
            return True
        b = self.batch_of(t)
        return b is None or b.owner < 0 or b.owner == p.team

    def _batches(self):
        for b in self.batches.values():
            if not b.paid and not b.failed and self.time > b.deadline:
                b.failed = True

    def _zombie_spawns(self, dt):
        self.zombie_timer -= dt
        if self.zombie_timer > 0:
            return
        self.zombie_timer = (6 - self.phase) / self.map.scale / self.cfg["zombieSpawnMul"]
        self.use_stream(S_ZOMBIE, self.zombie_spawn_index); self.zombie_spawn_index += 1
        cap = _round((self.cfg["zombieCapBase"] + self.cfg["zombieCapGrowth"] * self.time / self.cfg["matchDuration"]) * self.map.scale)
        if sum(1 for z in self.zombies if z.from_player < 0) < cap:
            self.spawn_zombie_at_infected()

    def spawn_zombie_at_infected(self):
        pool = [n for n in self.map.nodes if n.infection >= F05 and not n.safe]
        if pool:
            self.spawn_zombie(pool[self.rng.next(len(pool))].id, -1)

    def spawn_zombie(self, node, from_player):
        z = Zombie(self._id("zombie"), node, self.map.nodes[node].pos, from_player, DotNetRandom(self.rng.next()))
        self.zombies.append(z)
        return z

    def _events(self, dt):
        m = self.map
        self.event_timer -= dt
        if self.event_timer > 0:
            return
        self.event_timer = 25 - 3 * self.phase
        self.use_stream(S_EVENT, self.event_index); self.event_index += 1
        roll, ph = self.rng.next_double(), self.phase
        if roll < 0.3:
            self.spawn_task(True, 0.0); return
        if roll < 0.5:
            sup = [n for n in m.nodes if n.type == "supply"]
            s = sup[self.rng.next(len(sup))]
            s.medkits = min(8, s.medkits + 3)
            self.log("supply", f"物資空投：補給站 {s.id} 新增醫療包（剩 {s.medkits}）")
            return
        if ph == 0:
            self.spawn_task(True, 0.0); return
        if roll < 0.75:
            pool = [n for n in m.nodes if n.type == "road" and n.id != m.start_node]
            n = pool[self.rng.next(len(pool))]
            n.infection = 1.0
            for _ in range(2 + ph):
                self.spawn_zombie(n.id, -1)
            self.city_infection += 2
            self.log("outbreak", f"感染爆發！出現 {2 + ph} 隻殭屍", n.id)
            return
        opn = [e for e in m.edges if not e.blocked]
        brs = [e for e in opn if e.bridge]
        e = brs[self.rng.next(len(brs))] if brs and self.rng.next_double() < 0.5 else opn[self.rng.next(len(opn))]
        e.blocked = True
        m.recompute()
        if not m.connected():
            e.blocked = False
            m.recompute()
            return
        e.blocked_until = self.time + 45 + self.rng.next_double() * 45
        self.log("failure", f"有一段{'橋' if e.bridge else '道路'}壞了，{e.blocked_until - self.time:.0f} 秒後修好")

    def _roadblocks(self, dt):
        self.roadblock_timer -= dt
        if self.roadblock_timer > 0:
            return
        c, m = self.cfg, self.map
        self.roadblock_timer = c["roadblockInterval"] / (1 + 0.15 * self.phase)
        self.use_stream(S_BLOCK, self.roadblock_index)
        self.roadblock_index += 1
        opn = [e for e in m.edges if not e.blocked and not e.bridge]
        if not opn:
            return
        for _ in range(5):
            e = opn[self.rng.next(len(opn))]
            e.blocked = True
            m.recompute()
            if not m.connected():
                e.blocked = False
                m.recompute()
                continue
            e.blocked_until = self.time + c["roadblockTime"] * (0.75 + 0.5 * self.rng.next_double())
            self.log("roadblock", f"屍潮堵住了路口 {e.a} 和 {e.b} 之間的路，{e.blocked_until - self.time:.0f} 秒後才散開", e.a)
            return

    def incident_by_id(self, i):
        for inc in self.incidents:
            if inc.id == i:
                return inc
        return None

    def _spawn_incidents(self, dt):
        c, m = self.cfg, self.map
        self.incident_timer -= dt
        if self.incident_timer > 0 or self.ready_at >= 0:
            return
        n = max(1, len(self.players))
        self.incident_timer = c["incidentInterval"] * self.dscale / (1 + 0.2 * self.phase) / max(1.0, (n / 6.0) ** 0.5)
        self.use_stream(S_EVENT, self.event_index); self.event_index += 1
        if sum(1 for i in self.incidents if i.status == "active" and i.kind != "generator") >= 1 + n // 4:
            return
        ph = self.phase
        kinds = [("outbreak", 0.3), ("airdrop", 0.25), ("trapped", 0.2), ("bridge", 0.15), ("chest", 0.15)]
        if ph >= 1:
            kinds += [("guard", 0.15), ("napalm", 0.15), ("decoy", 0.12)]
        if ph >= 2:
            kinds += [("heli", 0.15), ("boss", 0.12)]
        themes = sorted({nd.district for nd in m.nodes}, key=DISTRICTS.index)
        kinds += [(DISTRICT_EVENT[d], 0.24 / len(themes)) for d in themes]
        kinds.append(("generator", c["generatorWeight"]))
        roll = self.rng.next_double() * sum(w for _, w in kinds)
        kind = kinds[-1][0]
        for k, w in kinds:
            if roll < w:
                kind = k
                break
            roll -= w
        roads = [nd for nd in m.nodes if nd.type == "road" and nd.id != self.extraction]
        theme = next((d for d, k in DISTRICT_EVENT.items() if k == kind), None)
        if theme is not None:
            pool = [nd for nd in roads if nd.district == theme] or roads
            node = pool[self.rng.next(len(pool))].id
        elif kind == "generator":
            gens = self.generators()
            if not gens:
                return
            g = gens[self.rng.next(len(gens))]
            if not g.powered or self.time - self.last_generator_break < c["generatorCooldown"]:
                return
            self._break_generator(g)
            self.log("incident", f"突發事件：{INCIDENTS['generator'][0]}! 工廠停工了!", g.id)
            return
        elif kind in ("guard", "heli"):
            node = self.extraction
        elif kind == "trapped":
            pool = [nd for nd in roads if nd.infection >= F03] or roads
            node = pool[self.rng.next(len(pool))].id
        elif kind == "bridge":
            opn = [e for e in m.edges if not e.blocked]
            brs = [e for e in opn if e.bridge] or opn
            e = brs[self.rng.next(len(brs))]
            e.blocked = True
            m.recompute()
            if not m.connected():
                e.blocked = False
                m.recompute()
                return
            node = e.a
        else:
            node = roads[self.rng.next(len(roads))].id
        inc = Incident(self._id("incident"), kind, node, self.time)
        if kind == "bridge":
            inc.edge = e.id
            e.blocked_until = inc.deadline + 120.0
        if kind == "outbreak":
            m.nodes[node].infection = 1.0
            for _ in range(2 + ph):
                self.spawn_zombie(node, -1)
        if kind == "boss":
            for _ in range(2):
                self.spawn_zombie(node, -1)
        self.incidents.append(inc)
        self.log("incident", f"突發事件：{inc.name}!", node)

    def generators(self):
        return [n for n in self.map.nodes if n.type == "generator"]

    def _west(self, node):
        return self.map.west(node)

    def _generators_for(self, factory):
        gens = self.generators()
        same = [g for g in gens if self._west(g.id) == self._west(factory.id)]
        return same or gens

    def powered(self, n):
        if n.type == "generator":
            return n.powered
        return n.type == "factory" and (not self.cfg["escape"] or any(g.powered for g in self._generators_for(n)))

    def _break_generator(self, g):
        g.powered = False
        self.last_generator_break = self.time
        self.incidents.append(Incident(self._id("incident"), "generator", g.id, self.time))

    def _update_generators(self):
        ph = self.phase
        if ph == self.last_phase:
            return
        self.last_phase = ph
        if ph < 1 or ph > 2 or self.ready_at >= 0:
            return
        gens = self.generators()
        if not gens:
            return
        g = gens[(ph - 1) % len(gens)]
        if not g.powered:
            return
        self._break_generator(g)
        self.log("incident", f"突發事件：{INCIDENTS['generator'][0]}! 工廠停工了!", g.id)

    def _spawn_civilians(self):
        m = self.map
        self.use_stream(S_TASK, 300000)
        pool = [n for n in m.nodes if n.type == "road" and n.id != self.extraction and 4 <= m.dist[self.extraction, n.id] < INF]
        if not pool:
            return
        count = min(60, max(6, _round(len(self.players) * self.cfg["civiliansPerPlayer"])))
        for _ in range(count):
            self._add_civilian(pool[self.rng.next(len(pool))].id)

    def _add_civilian(self, origin):
        m, c = self.map, self.cfg
        d = float(m.dist[origin, self.extraction])
        if d >= INF:
            return
        base = (18 + d * 0.5 + 20 * m.nodes[origin].infection) * c["rescueRewardMul"] * (1 + 0.25 * self.phase)
        self.tasks.append(Task(id=self._id("task"), type="rescue", origin=origin, dest=self.extraction, weight=4,
                               reward=_round(base), available_at=self.time, expires_at=-1.0, status="open"))

    def _restart_factories(self, g):
        c, m, k = self.cfg, self.map, 0
        for f in m.nodes:
            if f.type != "factory" or g not in self._generators_for(f):
                continue
            d = float(m.dist[f.id, self.extraction])
            if d >= INF:
                continue
            weight = 3
            base = (10 + d * 0.6 + weight * c["partWeightReward"]) * (1 + 0.25 * self.phase)
            self.tasks.append(Task(id=self._id("task"), type="part", origin=f.id, dest=self.extraction, weight=weight,
                                   reward=_round(base), available_at=self.time, expires_at=self.time + 90, status="open"))
            k += 1
        return k

    def _check_medevac(self, dt):
        self.medevac_timer -= dt
        if self.medevac_timer > 0:
            return
        self.medevac_timer = 5.0
        n = len(self.players)
        down = sum(1 for p in self.players if p.state in ("downed", "eliminated"))
        if down < max(2, -(-n // 4)) or self.time < self.medevac_next or \
                any(i.kind == "medevac" and i.status == "active" for i in self.incidents):
            return
        self.use_stream(S_EVENT, 200000 + self.medevac_rolls); self.medevac_rolls += 1
        if self.rng.next_double() >= 0.5:
            return
        m = self.map
        pool = [nd for nd in m.nodes if nd.type == "road" and 4 <= m.dist[self.extraction, nd.id] <= 15 and self.in_zone(nd.pos)] or \
               [nd for nd in m.nodes if nd.type == "road" and nd.id != self.extraction]
        node = pool[self.rng.next(len(pool))].id
        inc = Incident(self._id("incident"), "medevac", node, self.time)
        self.incidents.append(inc)
        self.medevac_next = inc.deadline + 60.0
        self.log("incident", f"突發事件：{inc.name}! 接應到就讓倒下的人全部復活!", node)

    def _update_incidents(self, dt):
        for inc in self.incidents:
            if inc.status != "active":
                continue
            workers = [p for p in self.players if p.state == "active" and p.next_node < 0 and p.node == inc.node
                       and p.pending is not None and p.pending["action"] == "fix" and p.pending["task"] == inc.id]
            inc.workers = len(workers)
            if workers:
                for p in workers:
                    inc.progress += dt
                    inc.work[p.id] = inc.work.get(p.id, 0.0) + dt
            if inc.progress >= inc.need:
                self._solve(inc, workers)
            elif self.time >= inc.deadline:
                self._fail_incident(inc)
        self.incidents = [i for i in self.incidents if i.status == "active" or self.time - i.deadline < 10]

    def _combo_gain(self, p, base):
        c = self.cfg
        prev = p.combo
        gap = self.time - p.last_score_at
        if gap > c["comboWindow"]:
            p.combo = 0
        elif gap >= 2.0:
            p.combo = min(c["comboMax"], p.combo + 1)
        p.last_score_at = self.time
        if p.combo != prev and p.combo in (2, c["comboMax"]):
            self.log("combo", f"{p.name} 連續得分 {p.combo + 1} 次！", p.node)
        return base + base * p.combo * c["comboStep"] // 100

    def _solver_names(self, inc):
        names = [self.player_by_id(pid).name for pid, _ in sorted(inc.work.items(), key=lambda kv: -kv[1])]
        if not names:
            return "大家"
        if len(names) == 1:
            return names[0]
        if len(names) <= 3:
            return "、".join(names[:-1]) + " 和 " + names[-1]
        return f"{names[0]}、{names[1]} 等 {len(names)} 人"

    def combo_of(self, p):
        return p.combo if self.time - p.last_score_at <= self.cfg["comboWindow"] else 0

    def _solve(self, inc, workers):
        c, m = self.cfg, self.map
        inc.status = "solved"
        total = sum(inc.work.values()) or 1.0
        reward = _round(inc.reward * (1 + 0.25 * self.phase) * c["incidentRewardMul"])
        for pid, w in inc.work.items():
            q = self.player_by_id(pid)
            q.score += self._combo_gain(q, _round(reward * w / total))
            q.fixed += 1
        for p in workers:
            p.pending, p.action_timer = None, 0.0
        pos = m.nodes[inc.node].pos
        if inc.kind == "outbreak":
            self.zombies = [z for z in self.zombies if _vd(z.pos, pos) > 4.0]
            m.nodes[inc.node].infection = 0.0
            effect = "當地殭屍清除、感染歸零!"
        elif inc.kind == "bridge":
            e = m.edges[inc.edge] if 0 <= inc.edge < len(m.edges) else None
            if e is not None:
                e.blocked_until = self.time
            self.speed_boost_until = self.time + c["speedBoostTime"]
            effect = f"全員加速 {c['speedBoostTime']:.0f} 秒!"
        elif inc.kind == "airdrop":
            for p in self.players:
                if p.state == "active":
                    p.hp = 100.0
            for n in m.nodes:
                if n.type == "supply":
                    n.medkits = max(n.medkits, 5)
            effect = "全員生命回滿、補給站醫療包補滿!"
        elif inc.kind == "trapped":
            for _ in range(c["trappedCivilians"]):
                self._add_civilian(inc.node)
            effect = f"{c['trappedCivilians']} 位民眾等著被帶回撤離點!"
        elif inc.kind == "guard":
            self.zombies = [z for z in self.zombies if _vd(z.pos, pos) > 6.0]
            effect = "守衛擊退了撤離點附近的殭屍!"
        elif inc.kind == "medevac":
            back = [q for q in self.players if q.state in ("downed", "eliminated")]
            for q in back:
                self._respawn(q, quiet=True)
            effect = f"{len(back)} 位倒下的同學全部復活!"
        elif inc.kind == "napalm":
            n0 = len(self.zombies)
            self.zombies = [z for z in self.zombies if _vd(z.pos, pos) > 8.0]
            effect = f"炸掉了附近 {n0 - len(self.zombies)} 隻殭屍!"
        elif inc.kind == "decoy":
            self.zombie_stun_until = self.time + c["stunTime"]
            effect = f"全城殭屍暫停 {c['stunTime']:.0f} 秒!"
        elif inc.kind == "chest":
            extra = max(1, _round(self.parts_required * 0.04))
            self.parts += extra
            effect = f"寶箱裡有 {extra} 個零件和 {reward} 分!"
        elif inc.kind == "herbs":
            for q in self.players:
                if q.state == "active":
                    q.medkits += 1
                    q.hp = min(100.0, q.hp + 30)
            effect = "全員多一個醫療包、生命 +30!"
        elif inc.kind == "gas":
            if self.ready_at >= 0:
                self.fuel_required = max(1, self.fuel_required - 1)
            else:
                self.fuel_discount += 1
            effect = "之後少要 1 桶油!"
        elif inc.kind == "rubble":
            extra = max(1, _round(self.parts_required * 0.05))
            self.parts += extra
            effect = f"挖出 {extra} 個零件!"
        elif inc.kind == "bell":
            town = [t for t in self.tasks if t.type == "rescue" and t.status in ("open", "announced")
                    and m.nodes[t.origin].district == "town"]
            for t in town:
                t.status, t.expires_at = "done", self.time
            self.evacuated += len(town)
            effect = f"鎮民聽到鐘聲! {len(town)} 名平民逃到避難所"
        elif inc.kind == "boss":
            n0 = len(self.zombies)
            self.zombies = [z for z in self.zombies if _vd(z.pos, pos) > 6.0]
            effect = f"殭屍王倒下! 附近 {n0 - len(self.zombies)} 隻殭屍一同陣亡!"
        elif inc.kind == "generator":
            m.nodes[inc.node].powered = True
            inc.deadline = self.time
            effect = f"工廠恢復生產零件!"
        else:
            self.heli_boost = True
            if self.ready_at >= 0:
                self.fuel_required = max(1, self.fuel_required - 1)
                effect = "直升機少需要 1 桶油!"
            else:
                less = max(1, _round(self.parts_required * 0.05))
                self.parts_required = max(self.parts + 1, self.parts_required - less)
                effect = f"零件需求 -{less}!"
        ok = INCIDENT_TEXT.get(inc.kind, (f"解決了{inc.name}!", ""))[0]
        self.log("solved", f"{self._solver_names(inc)} {ok} {effect}", inc.node)

    def _fail_incident(self, inc):
        c, m = self.cfg, self.map
        inc.status = "failed"
        if inc.kind == "outbreak":
            for _ in range(3):
                self.spawn_zombie(inc.node, -1)
            effect = "殭屍又多了 3 隻!"
        elif inc.kind == "bridge":
            effect = "橋要再斷 2 分鐘!"
        elif inc.kind == "airdrop":
            for _ in range(2):
                self.spawn_zombie(inc.node, -1)
            effect = "物資被殭屍佔領!"
        elif inc.kind == "trapped":
            for _ in range(4):
                self.spawn_zombie(inc.node, -1)
            effect = "受困的民眾變成了 4 隻殭屍!"
        elif inc.kind == "guard":
            self.guard_down_until = self.time + c["guardDownTime"]
            effect = f"撤離點守衛倒下! {c['guardDownTime']:.0f} 秒內無法攻擊殭屍!"
        elif inc.kind == "medevac":
            effect = "醫療隊撤走了!"
        elif inc.kind in ("napalm", "decoy"):
            for _ in range(2):
                self.spawn_zombie(inc.node, -1)
            effect = "訊號引來 2 隻殭屍!"
        elif inc.kind == "chest":
            effect = "寶箱被殭屍叼走了!"
        elif inc.kind == "herbs":
            for _ in range(2):
                self.spawn_zombie(inc.node, -1)
            effect = "草叢裡竄出 2 隻殭屍!"
        elif inc.kind == "gas":
            effect = "這次沒裝到油!"
        elif inc.kind == "rubble":
            for _ in range(2):
                self.spawn_zombie(inc.node, -1)
            effect = "瓦礫裡爬出 2 隻殭屍!"
        elif inc.kind == "bell":
            for _ in range(3):
                self.spawn_zombie(inc.node, -1)
            effect = "鐘聲引來 3 隻殭屍!"
        elif inc.kind == "boss":
            for _ in range(5):
                self.spawn_zombie(inc.node, -1)
            effect = "殭屍王帶來 5 隻殭屍!"
        else:
            if self.ready_at >= 0:
                self.fuel_required += 1
                effect = "油料外洩，要多撿 1 桶油!"
            else:
                extra = max(1, _round(self.parts_required * 0.1))
                self.parts_required += extra
                effect = f"直升機故障! 零件需求 +{extra}!"
        bad = INCIDENT_TEXT.get(inc.kind, ("", f"{inc.name}沒有處理!"))[1]
        self.log("incident_fail", f"✕ {bad} {effect}", inc.node)

    def _restock(self, dt):
        self.restock_timer -= dt
        if self.restock_timer > 0:
            return
        self.restock_timer = 45.0
        for n in self.map.nodes:
            if n.type == "supply" and n.medkits < 5:
                n.medkits += 1
            elif n.type == "armory" and n.weapons < self.cfg["weaponStock"]:
                n.weapons += 1

    def _bots(self, dt):
        self.bot_timer -= dt
        if self.bot_timer > 0:
            return
        self.bot_timer = 0.25
        for p in self.players:
            if p.bot is None or p.state != "active":
                continue
            if not (p.plan_index >= len(p.plan) and p.pending is None and p.next_node < 0):
                p.idle_since = -1.0
                continue
            if p.idle_since < 0:
                p.idle_since = self.time
            if self.time - p.idle_since < self.cfg["botReactionTime"]:
                continue
            steps = p.bot.think(self, p)
            if self.time - p.last_plan_time >= self.cfg["planCooldown"] or self.is_emergency(steps):
                self.submit(p, steps)

    def task_by_id(self, i):
        for t in self.tasks:
            if t.id == i:
                return t
        return None

    def player_by_id(self, i):
        for p in self.players:
            if p.id == i:
                return p
        return None

    def load(self, p):
        return sum((self.task_by_id(i).weight if self.task_by_id(i) else 0) for i in p.cargo) + p.medkits + \
            (self.cfg["weaponWeight"] if p.ammo > 0 else 0)

    def speed(self, p):
        s = self.cfg["playerSpeed"] * (1 - self.cfg["loadSpeedPenalty"] * self.load(p))
        if p.hp < 30:
            s *= 0.8
        if self.time < self.speed_boost_until:
            s *= self.cfg["speedBoost"]
        return max(0.5, s)

    def is_emergency(self, steps):
        if not steps or not all(s["action"] in EMERGENCY for s in steps):
            return False
        if any(s["action"] in ("use_medkit", "heal") or s["action"] in DROPS for s in steps):
            return True
        moves = [s for s in steps if s["action"] in ("move", "goto")]
        return bool(moves) and 0 <= moves[-1]["node"] < len(self.map.nodes) and self.map.nodes[moves[-1]["node"]].safe

    def submit(self, p, steps, append=False, start_node=-1):
        c = self.cfg
        if self.status != "running":
            return "比賽尚未開始或已結束"
        if p.state != "active":
            return f"角色目前無法行動（{p.state}）"
        steps = [_step(**{k: s.get(k, d) for k, d in (("action", ""), ("node", -1), ("task", -1), ("target", -1), ("seconds", 0.0))})
                 for s in (steps or [])]
        for s in steps:
            s["action"] = str(s["action"]).strip().lower()
            s["node"], s["task"], s["target"], s["seconds"] = int(s["node"]), int(s["task"]), int(s["target"]), float(s["seconds"])
            if s["action"] not in ACTIONS:
                return "未知的 action：" + s["action"]
        emergency = not append and self.is_emergency(steps)
        if not emergency and self.time - p.last_plan_time < c["planCooldown"]:
            return f"送出太頻繁，請在 {c['planCooldown'] - (self.time - p.last_plan_time):.1f} 秒後再試（緊急撤退 / 治療指令不受限制）"
        if append:
            p.plan = p.plan[min(p.plan_index, len(p.plan)):] + steps
        else:
            p.plan = list(steps)
            here = p.next_node if p.next_node >= 0 else p.node
            if steps and steps[0]["action"] == "move" and 0 <= start_node < len(self.map.nodes) and start_node != here:
                p.plan.insert(0, _step("goto", node=start_node))
        p.plan = p.plan[:c["maxPlanSteps"]]
        p.plan_index = 0
        if not emergency:
            p.last_plan_time = self.time
        p.last_error = ""
        return None

    def _advance(self, ent, speed, dt):
        e = self.map.find_edge(ent.node, ent.next_node)
        ent.progress += speed * dt / max(0.01, e.length)
        if ent.progress < 1:
            ent.pos = _lerp(self.map.nodes[ent.node].pos, self.map.nodes[ent.next_node].pos, ent.progress)
            return
        ent.node, ent.next_node, ent.progress = ent.next_node, -1, 0.0
        ent.pos = self.map.nodes[ent.node].pos

    def _update_player(self, p, dt):
        if p.state == "eliminated":
            return
        if p.state == "downed":
            p.downed_timer -= dt
            if p.downed_timer <= 0:
                if self.doomed(p):
                    self._eliminate(p, p.eliminated_reason or "沒人來救")
                else:
                    self._respawn(p)
            return
        self._drop_now(p)
        if p.next_node >= 0:
            self._advance(p, self.speed(p), dt)
            return
        if p.pending is not None:
            p.action_timer -= dt
            if p.action_timer <= 0:
                s, p.pending = p.pending, None
                err = None if s["action"] == "fix" else self._validate(p, s)
                if err:
                    self._fail(p, s, err, True)
                else:
                    self._complete(p, s)
            return
        for _ in range(8):
            if not (p.plan_index < len(p.plan) and p.next_node < 0 and p.pending is None):
                break
            self._start_step(p, p.plan[p.plan_index])

    def _start_step(self, p, s):
        m, a = self.map, s["action"]
        if a == "move":
            if s["node"] == p.node:
                p.plan_index += 1; return
            if not 0 <= s["node"] < len(m.nodes):
                self._fail(p, s, f"move：節點 {s['node']} 不存在"); return
            e = m.find_edge(p.node, s["node"])
            if e is None:
                self._fail(p, s, f"move：{p.node} 與 {s['node']} 之間沒有道路"); return
            if e.blocked:
                self._fail(p, s, f"move：道路 {p.node}-{s['node']} 已封鎖"); return
            p.next_node, p.progress = s["node"], 0.0
            p.plan_index += 1
            return
        if a == "goto":
            if not 0 <= s["node"] < len(m.nodes):
                self._fail(p, s, f"goto：節點 {s['node']} 不存在"); return
            if s["node"] == p.node:
                p.plan_index += 1; return
            hop = int(m.next[p.node, s["node"]])
            if hop < 0:
                self._fail(p, s, f"goto：無法抵達節點 {s['node']}"); return
            p.next_node, p.progress = hop, 0.0
            return
        if a in DROPS:
            self._do_drop(p, s)
            p.plan_index += 1
            return
        t = self.task_by_id(s["task"]) if s["task"] >= 0 else None
        if (a == "pickup" and s["task"] in p.cargo) or (a == "dropoff" and t is not None and t.completed_by == p.id):
            p.plan_index += 1; return
        if a == "pickup" and t is not None and t.status == "announced" and t.origin == p.node and self.can_take(p, t):
            return
        err = self._validate(p, s)
        if err:
            self._fail(p, s, err); return
        p.pending = s
        p.action_timer = {"heal": 4.0, "revive": 3.0, "use_medkit": 1.5}.get(
            a, min(30.0, max(0.1, s["seconds"])) if a == "wait" else min(30.0, max(1.0, s["seconds"] or 10.0)) if a == "fix" else 1.0)
        p.plan_index += 1

    def _drop_now(self, p):
        while p.plan_index < len(p.plan) and p.plan[p.plan_index]["action"] in DROPS:
            self._do_drop(p, p.plan[p.plan_index])
            p.plan_index += 1

    def _do_drop(self, p, s):
        at = p.next_node if p.next_node >= 0 and p.progress > 0.5 else p.node
        a = s["action"]
        if a == "drop_medkit":
            if p.medkits > 0:
                p.medkits -= 1
            else:
                p.last_error = f"[{self.time:.0f}s] drop_medkit：沒有醫療包（已略過此步）"
            return
        if a == "drop_weapon":
            if p.ammo > 0:
                p.ammo = 0
            else:
                p.last_error = f"[{self.time:.0f}s] drop_weapon：沒有武器（已略過此步）"
            return
        t = self.task_by_id(s["task"])
        if t is None or s["task"] not in p.cargo:
            p.last_error = f"[{self.time:.0f}s] drop：你沒有攜帶委託 #{s['task']}（已略過此步）"
            return
        p.cargo.remove(t.id)
        t.status, t.carrier, t.origin = "open", -1, at
        t.expires_at = -1.0 if t.type == "fuel" else self.time + 60
        for k in range(len(p.plan) - 1, p.plan_index, -1):
            if p.plan[k]["action"] == "dropoff" and p.plan[k]["task"] == t.id:
                del p.plan[k]

    def _weapons(self, dt):
        c = self.cfg
        for p in self.players:
            if p.state != "active" or p.ammo <= 0:
                continue
            p.fire_cooldown -= dt
            if p.fire_cooldown > 0:
                continue
            best, bd = None, c["weaponRange"]
            for z in self.zombies:
                d = _vd(z.pos, p.pos)
                if d <= bd:
                    best, bd = z, d
            if best is None:
                continue
            self.zombies.remove(best)
            p.ammo -= 1
            p.kills += 1
            p.score += c["killReward"]
            p.fire_cooldown = c["weaponInterval"]
            self.player_shots = (self.player_shots + [(p.pos, best.pos, self.time)])[-30:]

    def _validate(self, p, s):
        node, a, cap = self.map.nodes[p.node], s["action"], self.cfg["capacity"]
        if a == "pickup":
            t = self.task_by_id(s["task"])
            if t is None or t.status != "open":
                return f"pickup：委託 #{s['task']} 已不可領取"
            if t.origin != p.node:
                return f"pickup：委託 #{s['task']} 在節點 {t.origin}，你在 {p.node}"
            if not self.can_take(p, t):
                return f"pickup：批次合約 B{t.batch} 已由別人負責"
            if self.load(p) + t.weight > cap:
                return f"pickup：背包容量不足（{self.load(p)}+{t.weight}>{cap}）"
        elif a == "dropoff":
            t = self.task_by_id(s["task"])
            if t is None or s["task"] not in p.cargo:
                return f"dropoff：你沒有攜帶委託 #{s['task']}"
            if t.dest != p.node:
                return f"dropoff：委託 #{s['task']} 目的地是 {t.dest}，你在 {p.node}"
        elif a == "take_medkit":
            if node.type != "supply":
                return "take_medkit：這裡不是補給站"
            if node.medkits <= 0:
                return "take_medkit：補給站已無醫療包"
            if self.load(p) + 1 > cap:
                return "take_medkit：背包容量不足"
        elif a == "take_weapon":
            if node.type != "armory":
                return "take_weapon：這裡不是軍火庫"
            if node.weapons <= 0:
                return "take_weapon：軍火庫沒有武器了"
            if p.ammo > 0:
                return "take_weapon：你已經有武器了"
            if self.load(p) + self.cfg["weaponWeight"] > cap:
                return "take_weapon：背包容量不足"
        elif a == "use_medkit":
            return None if p.medkits > 0 else "use_medkit：沒有醫療包"
        elif a == "heal":
            return None if node.type == "hospital" else "heal：這裡不是醫院"
        elif a == "fix":
            inc = self.incident_by_id(s["task"])
            if inc is None or inc.status != "active":
                return f"fix：事件 #{s['task']} 已經結束"
            if inc.node != p.node:
                return f"fix：事件 #{s['task']} 在節點 {inc.node}，你在 {p.node}"
        elif a == "revive":
            t = self.player_by_id(s["target"])
            if t is None or t.state != "downed":
                return f"revive：玩家 {s['target']} 不是倒地狀態"
            if t.node != p.node:
                return f"revive：玩家 {s['target']} 在節點 {t.node}"
        return None

    def _complete(self, p, s):
        a, c = s["action"], self.cfg
        if a == "pickup":
            t = self.task_by_id(s["task"])
            t.status, t.carrier = "carried", p.id
            p.cargo.append(t.id)
            self._noise(p.node, c["actionNoiseRadius"], c["actionNoiseDuration"], "pickup")
            b = self.batch_of(t)
            if b is not None and b.owner < 0 and not c["coop"]:
                b.owner = p.team
                for i in b.tasks:
                    bt = self.task_by_id(i)
                    if bt:
                        bt.expires_at = max(bt.expires_at, b.deadline + 60)
                self.log("batch", f"{p.name} 承接批次合約 B{b.id}")
        elif a == "dropoff":
            t = self.task_by_id(s["task"])
            p.cargo.remove(t.id)
            if not (c["escape"] and p.node == self.extraction):
                self._noise(p.node, c["actionNoiseRadius"], c["actionNoiseDuration"], "dropoff")
            useless = c["escape"] and ((t.type == "part" and self.ready_at >= 0) or (t.type == "fuel" and self.fuel >= self.fuel_required))
            gain = t.reward
            on_time = not useless and t.urgent and self.time <= t.deadline
            if on_time:
                gain += t.bonus
            if c["escape"] and t.type == "part" and c["partDecay"] > 0:
                gain = _round(gain * c["partDecay"] / (c["partDecay"] + p.parts))
            if useless:
                gain = 0
            elif c["escape"]:
                gain = self._combo_gain(p, gain)
            p.score += gain
            t.status, t.carrier, t.completed_by, t.expires_at = "done", -1, p.id, self.time
            if c["coop"] and not c["escape"]:
                self._reduce_city(p, gain)
            if t.type == "part":
                self.parts += t.weight
                p.parts += t.weight
            elif t.type == "fuel":
                self.fuel += 1
                p.fuel += 1
            if t.type == "rescue":
                p.rescued += 1
                self.evacuated += 1
                if not c["coop"]:
                    self.city_infection = max(0.0, self.city_infection - 1.5)
            elif t.type not in ("part", "fuel"):
                p.delivered += 1
            kind = {"rescue": "救援", "part": "零件", "fuel": "油桶"}.get(t.type, "配送")
            combo = f"（連續得分 {p.combo + 1} 次）" if c["escape"] and p.combo > 0 else ""
            self.log("done", f"{p.name} 完成{kind} #{t.id} +{gain}{'（已經不需要了）' if useless else ''}{'（限時獎勵）' if on_time else ''}{combo}")
            b = self.batch_of(t)
            if b is not None and c["coop"]:
                b.delivered += 1
                b.by[p.id] = b.by.get(p.id, 0) + 1
                if not b.paid and not b.failed and b.delivered >= len(b.tasks):
                    b.paid = True
                    for pid, n in b.by.items():
                        q = self.player_by_id(pid)
                        share = _round(b.bonus * n / len(b.tasks))
                        q.score += share
                        self._reduce_city(q, share)
                    self.log("batch", f"批次合約 B{b.id} 準時完成！合約獎金 +{b.bonus}（{len(b.by)} 人合力）", p.node)
            elif b is not None and b.owner == p.team:
                b.delivered += 1
                if not b.paid and not b.failed and b.delivered >= len(b.tasks):
                    b.paid = True
                    p.score += b.bonus
                    self.log("batch", f"{p.name} 準時完成批次合約 B{b.id}，合約獎金 +{b.bonus}", p.node)
        elif a == "take_medkit":
            self.map.nodes[p.node].medkits -= 1
            p.medkits += 1
        elif a == "take_weapon":
            self.map.nodes[p.node].weapons -= 1
            p.ammo, p.fire_cooldown = c["weaponShots"], 0.0
        elif a == "use_medkit":
            p.medkits -= 1
            p.hp, p.infection = min(100.0, p.hp + 40), max(0.0, p.infection - 25)
        elif a == "heal":
            p.hp, p.infection = 100.0, max(0.0, p.infection - 40)
        elif a == "revive":
            t = self.player_by_id(s["target"])
            t.state, t.hp = "active", 40.0
            t.eliminated_reason = ""
            t.protect_until = self.time + 5.0
            fresh = self.time - t.revived_at >= 30.0
            t.revived_at = self.time
            bonus = c["reviveBonus"] if fresh else 0
            p.score += self._combo_gain(p, bonus) if c["escape"] and fresh else bonus
            if fresh:
                p.revives += 1
            self.log("revive", f"{p.name} 救起了 {t.name}" + (f" +{bonus}" if fresh else ""), p.node)

    def _fail(self, p, s, msg, advanced=False):
        p.last_error = f"[{self.time:.0f}s] {msg}（已略過此步）"
        if s["action"] == "move" and not advanced:
            end = p.plan_index
            while end + 1 < len(p.plan) and p.plan[end + 1]["action"] == "move":
                end += 1
            target = p.plan[end]["node"]
            del p.plan[p.plan_index:end + 1]
            if 0 <= target < len(self.map.nodes) and target != p.node and self.map.next[p.node, target] >= 0:
                p.plan.insert(p.plan_index, _step("goto", node=target))
            return
        if not advanced:
            p.plan_index += 1
        if s["action"] == "pickup":
            p.plan[p.plan_index:] = [x for x in p.plan[p.plan_index:] if not (x["action"] == "dropoff" and x["task"] == s["task"])]

    def _down(self, p, reason):
        c = self.cfg
        if p.next_node >= 0 and p.progress > 0.5:
            p.node = p.next_node
        p.next_node, p.progress = -1, 0.0
        p.pos = self.map.nodes[p.node].pos
        p.pending, p.plan, p.plan_index = None, [], 0
        p.state, p.downed_timer, p.hp = "downed", c["downedTime"], 0.0
        for i in p.cargo:
            t = self.task_by_id(i)
            if t:
                t.status, t.carrier, t.origin = "open", -1, p.node
                t.expires_at = -1.0 if t.type == "fuel" else self.time + 60
        p.cargo = []
        p.ammo = 0
        p.downs += 1
        penalty = c["respawnPenalty"] * (min(p.downs, 3) if c["escape"] else 1)
        p.score -= penalty
        p.combo, p.last_score_at = 0, -999.0
        self.city_infection += 1
        p.eliminated_reason = reason
        if self.doomed(p):
            self.log("down", f"{p.name} 在安全區外倒地了！{c['downedTime']:.0f} 秒內沒人來救就逃不掉了", p.node)
        else:
            self.log("down", f"{p.name} 倒地（{reason}），掉落貨物、扣 {penalty} 分，{c['downedTime']:.0f} 秒內有人來救 +{c['reviveBonus']}，不然回撤離點復活", p.node)

    def _respawn(self, p, quiet=False):
        c = self.cfg
        p.node, p.next_node, p.progress = (self.extraction if c["escape"] else self.map.start_node), -1, 0.0
        p.pos = self.map.nodes[p.node].pos
        p.state, p.hp, p.infection = "active", c["respawnHp"], min(p.infection, 30.0)
        p.protect_until = self.time + 5.0
        p.last_plan_time = -999.0
        p.eliminated_at, p.eliminated_reason = -1.0, ""
        p.plan, p.plan_index, p.pending = [], 0, None
        if quiet:
            return
        self.log("respawn", f"{p.name} 在{'撤離點' if c['escape'] else '避難所'}復活", p.node)

    def _eliminate(self, p, reason):
        if p.next_node >= 0 and p.progress > 0.5:
            p.node = p.next_node
        p.next_node = -1
        p.state, p.eliminated_at, p.eliminated_reason = "eliminated", self.time, reason
        p.plan, p.pending = [], None
        for i in p.cargo:
            t = self.task_by_id(i)
            if t:
                t.status, t.carrier, t.origin = "open", -1, p.node
                t.expires_at = -1.0 if t.type == "fuel" else self.time + 60
        p.cargo = []
        for b in self.batches.values():
            if b.owner == p.team:
                b.owner, b.failed = -1, True
        if self.cfg["escape"]:
            self.log("eliminated", f"{p.name} 倒下了（{reason}），沒能逃出", p.node)
            return
        self.spawn_zombie(p.node, p.id)
        self.city_infection += 4
        self.log("eliminated", f"{p.name} 倒下了（{reason}），化為殭屍，沒能逃出", p.node)

    def _update_zombie(self, z, dt):
        if z.next_node >= 0:
            self._advance(z, self.zombie_speed * (self.cfg["zombieChaseMul"] if z.mode == "chase" else 1.0), dt)
            return
        if z.wait > 0:
            z.wait -= dt
            if self._prey(z) is None:
                return
        self._decide(z)
        if z.target >= 0 and z.target != z.node:
            hop = int(self.map.next[z.node, z.target])
            if hop >= 0:
                z.next_node, z.progress = hop, 0.0
            else:
                z.target = -1

    def _decide(self, z):
        m = self.map
        prey = self._prey(z)
        if prey is not None:
            z.mode, z.chasing = "chase", prey.id
            z.target = prey.next_node if prey.next_node >= 0 else prey.node
            return
        z.chasing = -1
        near = [n for n in self.noises if _vd(z.pos, n.pos) <= n.radius]
        if near:
            z.mode, z.target = "attracted", min(near, key=lambda n: _vd(z.pos, n.pos)).node
            return
        if z.mode != "wander" or z.target < 0 or z.target == z.node:
            z.mode = "wander"
            cand = [n for n in m.nodes if not n.safe and 6 <= m.dist[z.node, n.id] <= 18] or \
                   [n for n in m.nodes if not n.safe and n.id != z.node]
            z.target = cand[z.rng.next(len(cand))].id if cand else -1

    def _targets(self, p):
        out = [s["task"] for s in p.plan[p.plan_index:] if s["action"] == "pickup"]
        if p.pending and p.pending["action"] == "pickup":
            out.append(p.pending["task"])
        return out

    def _fixing(self, p):
        out = [s["task"] for s in p.plan[p.plan_index:] if s["action"] == "fix"]
        if p.pending and p.pending["action"] == "fix":
            out.append(p.pending["task"])
        return out

    def zombie_path(self, z, max_len=10):
        path, frm = [], z.node
        if z.next_node >= 0:
            path.append(z.next_node)
            frm = z.next_node
        if z.target >= 0 and z.target != frm:
            path += self.map.path(frm, z.target) or []
        return path[:max_len]

    def _prey(self, z):
        prey, best = None, self.cfg["zombieSight"]
        for p in self.players:
            if p.state != "active" or (p.next_node < 0 and self.is_safe(p.node)):
                continue
            d = _vd(z.pos, p.pos)
            if d < best:
                best, prey = d, p
        return prey

    def _noise(self, node, radius, duration, source):
        for n in self.noises:
            if n.node == node:
                n.radius = max(n.radius, radius)
                if self.time + duration > n.until:
                    n.until, n.source = self.time + duration, source
                return
        self.noises.append(Noise(self._id("noise"), node, self.map.nodes[node].pos, radius, self.time + duration, source))

    def _noises(self, dt):
        self.noises = [n for n in self.noises if self.time < n.until]
        for p in self.players:
            if p.state != "active":
                continue
            p.noise_timer -= dt
            if p.noise_timer <= 0 and any(self.task_by_id(i) and self.task_by_id(i).type == "rescue" for i in p.cargo):
                p.noise_timer = 1.0
                self._noise(p.next_node if p.next_node >= 0 and p.progress > 0.5 else p.node, self.cfg["survivorNoiseRadius"], 2.0, "survivor")

    def _damage(self, dt):
        c = self.cfg
        scale = 0.7 + 0.15 * self.phase
        for p in self.players:
            if p.state != "active" or self.time < p.protect_until:
                continue
            here = self.map.nodes[p.node]
            if c["escape"] and not self.in_zone(p.pos):
                p.hp -= c["zoneDamage"] * dt
                p.infection += c["zoneInfection"] * dt
            if not (p.next_node < 0 and self.is_safe(p.node)) and self.time >= self.zombie_stun_until:
                hit = False
                for z in self.zombies:
                    if _vd(z.pos, p.pos) <= c["zombieAttackRange"]:
                        p.hp -= c["zombieDamage"] * scale * dt
                        p.infection += c["zombieInfection"] * scale * dt
                        hit = True
                if hit and self.time - p.last_scream > 3:
                    p.last_scream = self.time
                    self._noise(p.next_node if p.next_node >= 0 and p.progress > 0.5 else p.node, c["screamRadius"], 6.0, "scream")
                if p.next_node < 0 and here.infection > F07:
                    p.infection += 0.5 * dt
            if p.infection >= 100:
                p.infection = 100.0
                self._down(p, "感染值達到上限")
            elif p.hp <= 0:
                self._down(p, "被殭屍咬倒")

    def _reduce_city(self, p, points):
        d = min(self.city_infection, points * self.cfg["cityPerPoint"])
        self.city_infection -= d
        p.city_reduced += d

    def end(self, reason):
        self.status, self.end_reason = "ended", reason
        if self.cfg["escape"]:
            self.class_won = self.depart_at >= 0 and self.time >= self.depart_at
            if self.class_won:
                for p in self.players:
                    if p.state == "active" and p.next_node < 0 and p.node == self.extraction:
                        p.escaped = True
                        p.score += self.cfg["escapeBonus"] + _round(self.time_left() * self.cfg["earlyBonusPerSec"])
                n = sum(p.escaped for p in self.players)
                self.end_reason = f"直升機起飛！{n}/{len(self.players)} 人逃出城市"
            elif self.ready_at < 0:
                self.end_reason = f"直升機沒修好（零件 {self.parts}/{self.parts_required}），全班被困在城市裡"
            elif self.players and all(p.state == "eliminated" for p in self.players):
                self.end_reason = "所有人都倒下了，直升機沒能起飛"
            else:
                self.end_reason = f"油不夠（{self.fuel}/{self.fuel_required} 桶），直升機飛不起來"
            for i, p in enumerate(self.ranking()):
                p.rank = i + 1
            return
        self.class_won = self.cfg["coop"] and self.city_infection < 100
        if self.class_won:
            self.end_reason = "全班撐過了！城市守住了"
            for p in self.players:
                p.score += self.cfg["classBonus"]
        for p in self.players:
            if not self.cfg["escape"] and p.state == "active" and p.next_node < 0 and self.map.nodes[p.node].safe:
                p.score += self.cfg["survivalBonus"]
                p.evacuated = True
        for i, p in enumerate(self.ranking()):
            p.rank = i + 1

    TITLES = [("零件王", "parts"), ("救援王", "rescued"), ("事件王", "fixed"), ("油桶王", "fuel"), ("醫護王", "revives")]

    def ranking(self):
        return sorted(self.players, key=lambda p: (-p.score, p.state != "active",
                                                   -(1e30 if p.eliminated_at < 0 else p.eliminated_at), p.id))

    def replay_header(self, every):
        m = self.map
        return dict(every=every, extraction=self.extraction, duration=self.cfg["matchDuration"],
                    nodes=[[round(n.pos[0], 2), round(n.pos[1], 2), n.type] for n in m.nodes],
                    edges=[[e.a, e.b, 1 if e.bridge else 0] for e in m.edges],
                    players={str(p.id): [p.name, p.color] for p in self.players})

    def replay_frame(self):
        r2 = lambda v: round(v, 2)
        st = {"active": 0, "downed": 1, "eliminated": 2}
        return dict(
            t=r2(self.time), z=r2(self.zone_radius), parts=self.parts, preq=self.parts_required, fuel=self.fuel,
            freq=self.fuel_required, ready=r2(self.ready_at), depart=r2(self.depart_at),
            p=[[p.id, r2(p.pos[0]), r2(p.pos[1]), st.get(p.state, 0), p.score, round(p.hp)] for p in self.players],
            zb=[[r2(z.pos[0]), r2(z.pos[1]), 1 if z.mode == "chase" else 0] for z in self.zombies],
            k=[[t.origin, t.type[0], 1 if t.status == "announced" else 0] for t in self.tasks if t.status in ("open", "announced")],
            i=[[i.node, i.kind] for i in self.incidents if i.status == "active"],
            b=[e.id for e in self.map.edges if e.blocked],
            g=[n.id for n in self.map.nodes if n.type == "generator" and n.powered])

    def state_dict(self, viewer: Player | None):
        c, m = self.cfg, self.map

        def unit(p):
            return dict(id=p.id, name=p.name, state=p.state, node=p.node, next_node=p.next_node, hp=p.hp,
                        infection=p.infection, score=p.score, load=self.load(p), capacity=c["capacity"], medkits=p.medkits, ammo=p.ammo,
                        speed=self.speed(p), cargo=list(p.cargo), plan=[dict(s) for s in p.plan], plan_index=p.plan_index,
                        busy=p.pending["action"] if p.pending else ("moving" if p.next_node >= 0 else ""),
                        last_error=p.last_error, next_plan_at=p.last_plan_time + c["planCooldown"])
        you = {}
        if viewer is not None:
            you = unit(viewer)
        live = ("announced", "open", "carried")
        return dict(
            status=self.status, phase=self.phase, coop=c["coop"], class_won=self.class_won,
            escape=dict(enabled=c["escape"], extraction=self.extraction, parts=self.parts, parts_required=self.parts_required,
                        ready_at=self.ready_at, depart_at=self.depart_at, zone_x=m.nodes[self.extraction].pos[0],
                        zone_y=m.nodes[self.extraction].pos[1], zone_radius=self.zone_radius,
                        zone_damage=c["zoneDamage"], escape_bonus=c["escapeBonus"], evacuated=self.evacuated,
                        speed_boost_until=self.speed_boost_until, guard_down_until=self.guard_down_until,
                        zombie_stun_until=self.zombie_stun_until,
                        heli_boost=self.heli_boost, fuel=self.fuel, fuel_required=self.fuel_required),
            incidents=[dict(id=i.id, type=i.kind, name=i.name, node=i.node, deadline=i.deadline, progress=i.progress,
                            need=i.need, min_people=i.min_people, workers=i.workers, status=i.status,
                            reward=_round(i.reward * (1 + 0.25 * self.phase))) for i in self.incidents], phase_name=PHASE_KEYS[self.phase], end_reason=self.end_reason,
            seed=c["seed"], start_node=m.start_node, match_id=1, time=self.time, duration=c["matchDuration"],
            time_left=max(0.0, c["matchDuration"] - (self.depart_at - c["boardTime"] if c["escape"] and self.depart_at >= 0 else self.time)),
            city_infection=self.city_infection,
            rules=dict(capacity=c["capacity"], max_plan_steps=c["maxPlanSteps"], survival_bonus=c["survivalBonus"], respawn_penalty=c["respawnPenalty"],
                       player_speed=c["playerSpeed"], load_speed_penalty=c["loadSpeedPenalty"], plan_cooldown=c["planCooldown"],
                       downed_time=c["downedTime"], zombie_speed=c["zombieSpeed"], zombie_speed_per_phase=c["zombieSpeedPerPhase"],
                       zombie_sight=c["zombieSight"], zombie_attack_range=c["zombieAttackRange"], zombie_damage=c["zombieDamage"],
                       zombie_infection=c["zombieInfection"], zombie_chase_mul=c["zombieChaseMul"],
                       action_noise_radius=c["actionNoiseRadius"],
                       action_noise_duration=c["actionNoiseDuration"], survivor_noise_radius=c["survivorNoiseRadius"],
                       scream_radius=c["screamRadius"], weapon_weight=c["weaponWeight"], weapon_shots=c["weaponShots"],
                       kill_reward=c["killReward"], fuel_weight=c["fuelWeight"], weapon_range=c["weaponRange"], weapon_interval=c["weaponInterval"]),
            nodes=[dict(id=n.id, type=n.type, x=n.pos[0], y=n.pos[1], infection=n.infection, medkits=n.medkits, weapons=n.weapons, safe=n.safe,
                        powered=self.powered(n), district=n.district) for n in m.nodes],
            edges=[dict(id=e.id, a=e.a, b=e.b, length=e.length, blocked=e.blocked, bridge=e.bridge,
                        blocked_until=e.blocked_until if e.blocked else 0) for e in m.edges],
            tasks=[dict(id=t.id, type=t.type, origin=t.origin, dest=t.dest, weight=t.weight, reward=t.reward, bonus=t.bonus,
                        urgent=t.urgent, deadline=t.deadline, expires_at=t.expires_at, status=t.status, carrier=t.carrier,
                        batch=t.batch, available_at=t.available_at) for t in self.tasks if t.status in live],
            batches=[dict(id=b.id, origin=b.origin, owner=b.owner, bonus=b.bonus, delivered=b.delivered, deadline=b.deadline,
                          paid=b.paid, failed=b.failed, tasks=list(b.tasks)) for b in self.batches.values()
                     if any(self.task_by_id(i) and self.task_by_id(i).status in live for i in b.tasks)],
            players=[dict(id=p.id, name=p.name, color=p.color, state=p.state, bot=p.bot_kind, node=p.node,
                          next_node=p.next_node, progress=p.progress, x=p.pos[0], y=p.pos[1], hp=p.hp, infection=p.infection,
                          downed_timer=p.downed_timer, score=p.score, load=self.load(p), delivered=p.delivered, rescued=p.rescued, downs=p.downs,
                          city_reduced=p.city_reduced, role=p.role, targets=self._targets(p), parts=p.parts, escaped=p.escaped,
                          fixing=self._fixing(p), fixed=p.fixed, fuel=p.fuel, combo=self.combo_of(p), ammo=p.ammo, kills=p.kills)
                     for p in self.players],
            zombies=[dict(id=z.id, node=z.node, next_node=z.next_node, x=z.pos[0], y=z.pos[1], from_player=z.from_player,
                          progress=z.progress, mode=z.mode, target=z.target, chasing=z.chasing, path=self.zombie_path(z))
                     for z in self.zombies],
            noises=[dict(id=n.id, node=n.node, x=n.pos[0], y=n.pos[1], radius=n.radius, until=n.until, source=n.source) for n in self.noises],
            zombie_speed=self.zombie_speed,
            events=[dict(time=e["time"], type=e["type"], text=e["text"]) for e in self.events[-20:]],
            you=you,
        )

def classmate_policy():
    from apocalypse_sdk import CityGraph, drop, dropoff, fix, goto, heal, pickup, revive, use_medkit

    def policy(st):
        me = st.you
        g = CityGraph(st, zombie_penalty=3, zombie_lookahead=10)
        ex, depart = st.extraction, st.escape.get("depart_at", -1)
        if depart > 0 and depart - st.time < g.dist(st.here, ex) / me["speed"] + 15:
            if st.here == ex and st.is_idle:
                drops = [dropoff(t["id"]) for t in st.my_cargo() if t["dest"] == ex]
                return drops if drops and st.time >= me["next_plan_at"] else None
            going = me["plan"] and me["plan"][-1]["node"] == ex
            return None if going else [goto(ex)]
        if not st.is_idle:
            return None
        if me["hp"] < 40 or me["infection"] > 60:
            if me["medkits"] > 0:
                return [use_medkit()]
            return [goto(g.nearest(st.here, st.nodes_of("hospital"))), heal()]
        e = st.escape
        useless = [t for t in st.my_cargo() if (t["type"] == "part" and e["ready_at"] >= 0)
                   or (t["type"] == "fuel" and e["fuel"] >= e["fuel_required"])]
        if useless:
            return [drop(t["id"]) for t in useless]
        if st.time < me["next_plan_at"]:
            return None
        for q in st.players:
            if q["state"] == "downed" and q["id"] != me["id"]:
                eta = g.dist(st.here, q["node"]) / me["speed"]
                others = [g.dist(o["node"], q["node"]) for o in st.players if o["state"] == "active" and o["id"] not in (me["id"], q["id"])]
                if eta + 4 < q["downed_timer"] and g.dist(st.here, q["node"]) <= min(others, default=1e9):
                    return [goto(q["node"]), revive(q["id"])]
        if st.my_cargo():
            return [s for t in st.my_cargo() for s in (goto(t["dest"]), dropoff(t["id"]))]
        for inc in sorted(st.active_incidents(), key=lambda i: g.dist(st.here, i["node"])):
            eta = g.dist(st.here, inc["node"]) / me["speed"]
            if (eta < 25 or inc["type"] == "generator") and eta + 3 < inc["deadline"] - st.time and st.helpers(inc["id"]) < inc["min_people"]:
                return [goto(inc["node"]), fix(inc["id"], 20)]
        claimed = st.claimed_tasks()
        tasks = [t for t in st.open_tasks() if t["id"] not in claimed]
        if st.escape.get("ready_at", -1) >= 0:
            fuel = [t for t in tasks if t["type"] == "fuel"] if st.escape["fuel"] < st.escape["fuel_required"] else []
            if not fuel:
                return None if st.here == ex else [goto(ex)]
            tasks = fuel
        if not tasks:
            return None
        t = min(tasks, key=lambda t: (g.dist(st.here, t["origin"]) + g.dist(t["origin"], t["dest"])) / max(1, t["reward"]))
        return [goto(t["origin"]), pickup(t["id"]), goto(t["dest"]), dropoff(t["id"])]
    return policy

class LocalGame:

    def __init__(self, seed: int = 1, name: str = "我", frame_every: float = 3.0, classmates: int = 11,
                 classmate_policies=None, role: str = "", **cfg):
        self.sim = Sim(seed=seed, **cfg)
        self.me = self.sim.add_player(name)
        self.me.role = role
        self.agents = []
        pols = classmate_policies or [classmate_policy() for _ in range(classmates)]
        for i, pol in enumerate(pols):
            self.agents.append((self.sim.add_player(f"同學{i + 1}"), pol))
        self.frame_every = frame_every
        self.frames: list = []
        self.replay_every = 0.5
        self.replay_frames: list = []
        self.replay_head = None
        self.errors: list[str] = []

    def state(self, p=None):
        from apocalypse_sdk import State
        return State(self.sim.state_dict(p or self.me))

    def run(self, policy, interval: float = 1.0, latency: float = 0.3, dt: float = 0.05, verbose: bool = False):
        sim = self.sim
        sim.start()
        agents = [(self.me, policy)] + self.agents
        queues = {p.id: [] for p, _ in agents}
        import random
        jitter = random.Random(sim.cfg["seed"])

        offset = {p.id: (0.0 if p is self.me else jitter.random() * interval) for p, _ in agents}
        lag = {p.id: (latency if p is self.me else 0.05 + 0.35 * jitter.random()) for p, _ in agents}
        next_call = {p.id: offset[p.id] for p, _ in agents}
        next_frame = next_replay = 0.0
        self.replay_head = sim.replay_header(self.replay_every)
        while sim.status == "running":
            if sim.time >= next_frame:
                self.frames.append(self.state())
                next_frame += self.frame_every
            if sim.time >= next_replay:
                self.replay_frames.append(sim.replay_frame())
                next_replay += self.replay_every
            for p, pol in agents:
                if sim.time >= next_call[p.id]:
                    next_call[p.id] += interval
                    if p.state != "active":
                        continue
                    if p is not self.me and not (p.plan_index >= len(p.plan) and p.pending is None and p.next_node < 0):
                        continue
                    st = self.state(p)
                    t0 = _time.perf_counter()
                    plans = pol(st)
                    spent = _time.perf_counter() - t0 if p is self.me else 0.0
                    if isinstance(plans, dict):
                        plans = plans.get(0)
                    if plans is not None:
                        queues[p.id].append((sim.time + lag[p.id] + spent, plans, st.here))
            for p, _ in agents:
                q = queues[p.id]
                while q and q[0][0] <= sim.time:
                    _, steps, start = q.pop(0)
                    err = sim.submit(p, steps, start_node=start)
                    if err and verbose and p is self.me:
                        print(f"[{sim.time:5.0f}s] 送出失敗：{err}")
            if self.me.last_error and (not self.errors or self.errors[-1] != self.me.last_error):
                if verbose:
                    print("⚠", self.me.last_error)
                self.errors.append(self.me.last_error)
            sim.tick(dt)
        final = self.state()
        self.frames.append(final)
        self.replay_frames.append(sim.replay_frame())
        return final

    def replay_data(self):
        return dict(header=self.replay_head, frames=self.replay_frames)

    def result(self) -> dict:
        sim = self.sim
        me = self.me
        mates = [p.score for p, _ in self.agents]
        return dict(seed=sim.cfg["seed"], score=me.score, rank=me.rank, players=len(sim.players),
                    alive=me.evacuated, downs=me.downs, delivered=me.delivered, rescued=me.rescued,
                    end_reason=sim.end_reason, class_won=sim.class_won, ended_at=sim.time, escaped=me.escaped,
                    parts=me.parts, parts_total=sim.parts, parts_required=sim.parts_required, depart_at=sim.depart_at,
                    fuel_total=sim.fuel, fuel_required=sim.fuel_required, ready_at=sim.ready_at,
                    escaped_count=sum(p.escaped for p in sim.players),
                    scores=sorted((p.score for p in sim.players), reverse=True),
                    city=sim.city_infection, city_reduced=me.city_reduced,
                    class_avg=sum(mates) / len(mates) if mates else 0)
