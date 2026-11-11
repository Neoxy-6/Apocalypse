from __future__ import annotations

import heapq
import math
import time
import types
from typing import Callable, Iterable

import requests

__all__ = [
    "Game", "State", "CityGraph", "TripProblem", "ApiError", "benchmark", "test_local", "play", "draw", "replay", "replay_match", "load_map",
    "move", "goto", "pickup", "dropoff", "take_medkit", "use_medkit", "heal", "revive", "wait", "fix",
    "take_weapon", "drop", "drop_medkit", "drop_weapon",
]

def _step(action, node=-1, task=-1, target=-1, seconds=0.0):
    return {"action": action, "node": int(node), "task": int(task), "target": int(target), "seconds": float(seconds)}

def move(node):        return _step("move", node=node)
def goto(node):        return _step("goto", node=node)
def pickup(task):      return _step("pickup", task=task)
def dropoff(task):     return _step("dropoff", task=task)
def take_medkit():     return _step("take_medkit")
def use_medkit():      return _step("use_medkit")
def heal():            return _step("heal")
def revive(player_id): return _step("revive", target=player_id)
def wait(seconds=1.0): return _step("wait", seconds=seconds)
def fix(incident, seconds=10.0): return _step("fix", task=incident, seconds=seconds)
def take_weapon():     return _step("take_weapon")
def drop(task):        return _step("drop", task=task)
def drop_medkit():     return _step("drop_medkit")
def drop_weapon():     return _step("drop_weapon")

class ApiError(Exception):
    pass

class State:

    def __init__(self, raw: dict):
        self.raw = raw
        self.status: str = raw["status"]
        self.time: float = raw["time"]
        self.time_left: float = raw["time_left"]
        self.phase: int = raw["phase"]
        self.city_infection: float = raw["city_infection"]
        self.rules: dict = raw["rules"]

        self.you: dict = raw["you"]

        self.nodes: dict[int, dict] = {n["id"]: n for n in raw["nodes"]}
        self.edges: list[dict] = raw["edges"]
        self.tasks: dict[int, dict] = {t["id"]: t for t in raw["tasks"]}

        self.batches: dict[int, dict] = {b["id"]: b for b in raw.get("batches", [])}
        self.players: list[dict] = raw["players"]

        self.zombies: list[dict] = raw["zombies"]
        self.zombie_speed: float = raw.get("zombie_speed", 1.3)

        self.noises: list[dict] = raw.get("noises", [])
        self.events: list[dict] = raw["events"]

        self.incidents: list[dict] = raw.get("incidents", [])

        # 每個路口接下來 60 秒有殭屍在視線範圍內的時段：{路口: [(開始, 結束), ...]}，時間是 st.time 的單位
        self.danger: dict[int, list[tuple[float, float]]] = {
            d["node"]: [(d["windows"][i], d["windows"][i + 1]) for i in range(0, len(d["windows"]), 2)]
            for d in raw.get("danger", [])}

    @property
    def here(self) -> int:
        y = self.you
        return y["next_node"] if y["next_node"] >= 0 else y["node"]

    @property
    def is_idle(self) -> bool:
        y = self.you
        return y["plan_index"] >= len(y["plan"]) and y["busy"] == ""

    def open_tasks(self) -> list[dict]:
        return [t for t in self.tasks.values() if t["status"] == "open" and self.can_take(t)]

    def announced_tasks(self) -> list[dict]:
        return [t for t in self.tasks.values() if t["status"] == "announced" and self.can_take(t)]

    def can_take(self, t: dict) -> bool:
        b = self.tasks_batch(t)
        return b is None or b["owner"] in (-1, self.you.get("id", -1))

    @property
    def escape(self) -> dict:
        return self.raw.get("escape") or {"enabled": False}

    @property
    def extraction(self) -> int:
        return self.escape.get("extraction", -1)

    def in_zone(self, node: int) -> bool:
        e = self.escape
        if not e.get("enabled"):
            return True
        n = self.nodes[node]
        return (n["x"] - e["zone_x"]) ** 2 + (n["y"] - e["zone_y"]) ** 2 <= e["zone_radius"] ** 2

    def active_incidents(self) -> list[dict]:
        return [i for i in self.incidents if i["status"] == "active"]

    def helpers(self, incident_id: int) -> int:
        me = self.you.get("id", -1)
        return sum(1 for p in self.players if p["id"] != me and incident_id in p.get("fixing", []))

    def claimed_tasks(self) -> set[int]:
        me = self.you.get("id", -1)
        return {i for p in self.players if p["id"] != me for i in p.get("targets", [])}

    def tasks_batch(self, t: dict):
        return self.batches.get(t.get("batch", -1))

    def value_of(self, t: dict, arrive: float) -> int:
        """在 arrive 秒送到時這件值幾分（不含套件獎金）。過了 due_at 每秒少 late_per_sec，最低 min_reward。"""
        v = t["reward"]
        due = t.get("due_at", -1)
        if due >= 0 and arrive > due:
            v = max(t.get("min_reward", 0), round(t["reward"] - t.get("late_per_sec", 0) * (arrive - due)))
        if t.get("urgent") and arrive <= t.get("deadline", -1):
            v += t.get("bonus", 0)
        return v

    def set_of(self, t: dict) -> list[dict]:
        """和 t 同一套、還沒交的零件（包含 t 自己）。不是套件就回傳 [t]。
        長度比 t["set_size"] 少，代表有一件已經被交掉或消失，這套就拿不到獎金了。"""
        s = t.get("set", -1)
        if s < 0:
            return [t]
        return [x for x in self.tasks.values() if x.get("set", -1) == s]

    def danger_windows(self, node: int) -> list[tuple[float, float]]:
        """這個路口接下來哪些時段有殭屍在附近。沒有就是 []。"""
        return self.danger.get(node, [])

    def is_dangerous(self, node: int, t0: float, t1: float | None = None) -> bool:
        """t0 到 t1 秒之間（只給 t0 就是那一刻），這個路口有沒有殭屍在附近。"""
        t1 = t0 if t1 is None else t1
        return any(a <= t1 and t0 <= b for a, b in self.danger.get(node, []))

    def my_cargo(self) -> list[dict]:
        return [self.tasks[i] for i in self.you["cargo"] if i in self.tasks]

    def nodes_of(self, type_: str) -> list[int]:
        return [n["id"] for n in self.nodes.values() if n["type"] == type_]

    def trip_problem(self, g: "CityGraph | None" = None, max_tasks: int = 16) -> "TripProblem":
        """把這一趟能考慮的東西整理成 OR-Tools 能直接用的資料，見 TripProblem。"""
        return TripProblem(self, g, max_tasks)

    def powered(self, node_id: int) -> bool:
        return bool(self.nodes[node_id].get("powered", False))

    def __repr__(self):
        y = self.you
        return (f"<State {self.status} t={self.time:.0f}s phase={self.phase} city={self.city_infection:.0f}% "
                f"you@{y['node']} hp={y['hp']:.0f} inf={y['infection']:.0f} score={y['score']} "
                f"tasks={len(self.open_tasks())} zombies={len(self.zombies)}>")

class CityGraph:

    def __init__(self, state: State, danger_weight: float = 0.0, zombie_penalty: float = 0.0, zombie_radius: float = 3.0,
                 zombie_lookahead: float = 0.0, danger_penalty: float = 0.0, speed: float | None = None):
        # danger_penalty：照「走到那個路口的時間」查 st.danger，那時候有殭屍就多算幾格
        self.state = state
        self.danger_penalty = danger_penalty
        self.speed = speed or state.you.get("speed") or state.rules.get("player_speed", 3.0)
        self.adj: dict[int, list[tuple[int, float, float]]] = {n: [] for n in state.nodes}
        near = {n: 0.0 for n in state.nodes}
        if zombie_penalty > 0:
            for z in state.zombies:
                for nid, n in state.nodes.items():
                    if (n["x"] - z["x"]) ** 2 + (n["y"] - z["y"]) ** 2 <= zombie_radius ** 2:
                        near[nid] += 1
                if zombie_lookahead > 0:
                    t, px, py = 0.0, z["x"], z["y"]
                    for nid in z.get("path", []):
                        n = state.nodes[nid]
                        t += ((n["x"] - px) ** 2 + (n["y"] - py) ** 2) ** 0.5 / max(0.1, state.zombie_speed)
                        if t > zombie_lookahead:
                            break
                        near[nid] += 1 - t / zombie_lookahead
                        px, py = n["x"], n["y"]
        for e in state.edges:
            if e["blocked"]:
                continue
            a, b = e["a"], e["b"]
            inf = (state.nodes[a]["infection"] + state.nodes[b]["infection"]) / 2
            w = e["length"] * (1 + danger_weight * inf) + zombie_penalty * (near[a] + near[b])
            self.adj[a].append((b, w, e["length"]))
            self.adj[b].append((a, w, e["length"]))
        self._cache: dict[int, tuple[dict, dict]] = {}

    def dijkstra(self, src: int):
        if src not in self._cache:
            dist, prev, at = {src: 0.0}, {}, {src: self.state.time}
            pq = [(0.0, src)]
            while pq:
                d, u = heapq.heappop(pq)
                if d > dist.get(u, float("inf")):
                    continue
                for v, w, length in self.adj[u]:
                    t = at[u] + length / self.speed
                    nd = d + w
                    if self.danger_penalty > 0 and self.state.is_dangerous(v, t):
                        nd += self.danger_penalty
                    if nd < dist.get(v, float("inf")):
                        dist[v], prev[v], at[v] = nd, u, t
                        heapq.heappush(pq, (nd, v))
            self._cache[src] = (dist, prev)
        return self._cache[src]

    def dist(self, a: int, b: int) -> float:
        return self.dijkstra(a)[0].get(b, float("inf"))

    def path(self, a: int, b: int) -> list[int]:
        if a == b:
            return []
        dist, prev = self.dijkstra(a)
        if b not in dist:
            return []
        out = [b]
        while out[-1] != a:
            out.append(prev[out[-1]])
        return out[-2::-1]

    def path_steps(self, a: int, b: int) -> list[dict]:
        return [move(n) for n in self.path(a, b)]

    def matrix(self, nodes: Iterable[int]) -> list[list[float]]:
        nodes = list(nodes)
        return [[self.dist(a, b) for b in nodes] for a in nodes]

    def nearest(self, src: int, candidates: Iterable[int]):
        best = min(candidates, key=lambda n: self.dist(src, n), default=None)
        return best

class TripProblem:
    """這一趟的建模資料：直接對到 OR-Tools Routing 的各個東西。

    點的編號：START（你現在的位置）、END（撤離點），其他每件東西有一個「撿」點和一個「交」點。
    時間都是「從現在起算的秒數」（整數），分數都是整數。

      size          總共幾個點
      START, END    起點、終點的編號
      nodes[i]      第 i 個點在地圖上的路口
      time[i][j]    從 i 走到 j 要幾秒（含撿／交的 1 秒，已經避開殭屍）
      pairs         [(撿, 交), ...]：撿了就要送到
      task[i]       第 i 個點是哪件委託（START、END 是 None）
      value[i]      撿點 i：準時送到值幾分
      demand[i]     背包重量變化：撿點 +重量、交點 −重量
      capacity      背包上限；load 是現在背了多少
      open[i]       撿點 i 最早幾秒後才能撿
      due[i]        交點 i 的期限（幾秒後），沒有期限是 None
      late[i]       交點 i 晚一秒扣幾分
      danger[i]     撿點 i 有殭屍經過的時段 [(開始, 結束), ...]
      sets          [([撿點, ...], 套件獎金), ...]：同一套的撿點，一起撿才有獎金
      after         [(前一段的交點, 這一段的撿點, 加工秒數), ...]：要先把前一段送到加工廠，等加工完才能撿
      must          背包裡已經有的東西，它們的交點一定要去
    """

    START, END = 0, 1

    def __init__(self, st: "State", g: "CityGraph | None" = None, max_tasks: int = 16):
        self.st = st
        self.g = g or CityGraph(st, zombie_penalty=3, zombie_lookahead=10, danger_penalty=8)
        me, ex, now = st.you, st.extraction, st.time
        speed = max(0.5, me["speed"])
        e = st.escape
        self.capacity, self.load = st.rules["capacity"], me["load"]

        def useless(t):
            return (t["type"] in ("part", "delivery") and e.get("ready_at", -1) >= 0) or \
                   (t["type"] == "fuel" and e.get("fuel", 0) >= e.get("fuel_required", 0))

        follow = {t["after"]: t for t in st.tasks.values() if t.get("status") == "locked" and t.get("after", -1) >= 0}
        claimed = st.claimed_tasks()
        pool = [t for t in st.open_tasks() + st.announced_tasks()
                if t["id"] not in claimed and not useless(t) and t["weight"] <= self.capacity - self.load]

        def rate(t):
            d = self.g.dist(st.here, t["origin"]) + self.g.dist(t["origin"], t["dest"])
            nxt = follow.get(t["id"])
            if nxt:
                d += self.g.dist(nxt["origin"], ex)
            return (t["reward"] + (nxt["reward"] if nxt else 0)) / (d + 5)
        picked = sorted(pool, key=rate, reverse=True)[:max_tasks]
        for t in list(picked):  # 套件的其他件也放進來，才能一起排
            picked += [x for x in st.set_of(t) if x in pool and x not in picked]

        self.nodes, self.task, self.kind = [st.here, ex], [None, None], ["start", "end"]
        self.pairs, self.must, self.after = [], [], []

        def add_follow(t, drop):  # 送去加工的那件，後面那一段也一起排
            nxt = follow.get(t["id"])
            if nxt and not useless(nxt):
                pk = self._add(nxt["origin"], nxt, "pick")
                self.pairs.append((pk, self._add(nxt["dest"], nxt, "drop")))
                self.after.append((drop, pk, int(math.ceil(nxt.get("proc_delay", 0)))))
        for t in picked:
            pk, dr = self._add(t["origin"], t, "pick"), self._add(t["dest"], t, "drop")
            self.pairs.append((pk, dr))
            add_follow(t, dr)
        for t in st.my_cargo():
            if not useless(t):
                dr = self._add(t["dest"], t, "drop")
                self.must.append(dr)
                add_follow(t, dr)

        n = self.size = len(self.nodes)
        secs = lambda a, b: 0 if a == b else int(math.ceil(self.g.dist(a, b) / speed))
        self.time = [[0 if i == j else secs(self.nodes[i], self.nodes[j]) + (1 if self.kind[j] in ("pick", "drop") else 0)
                      for j in range(n)] for i in range(n)]
        self.value, self.demand, self.open = [0] * n, [0] * n, [0] * n
        self.due, self.late, self.danger = [None] * n, [0] * n, [[] for _ in range(n)]
        for i in range(n):
            t = self.task[i]
            if t is None:
                continue
            if self.kind[i] == "pick":
                self.value[i] = int(t["reward"])
                self.demand[i] = t["weight"]
                self.open[i] = max(0, int(math.ceil(t["available_at"] - now)))   # 還鎖著的是 0，由 after 決定
                self.danger[i] = [(max(0, int(a - now)), int(math.ceil(b - now))) for a, b in st.danger_windows(t["origin"]) if b > now]
            else:
                self.demand[i] = -t["weight"]
                if t.get("due_at", -1) >= 0:
                    self.due[i] = max(0, int(t["due_at"] - now))
                    self.late[i] = max(1, round(t.get("late_per_sec", 0)))
        groups = {}
        for pk, _ in self.pairs:
            s = self.task[pk].get("set", -1)
            if s >= 0:
                groups.setdefault(s, []).append(pk)
        self.sets = [(ks, self.task[ks[0]]["set_bonus"]) for ks in groups.values()
                     if len(ks) == self.task[ks[0]].get("set_size", 0) and len(ks) > 1]

    def _add(self, node, t, kind):
        self.nodes.append(node)
        self.task.append(t)
        self.kind.append(kind)
        return len(self.nodes) - 1

    def to_plan(self, order: list[int]) -> list[dict]:
        """把解出來的點的順序（不含 START、END 也可以）變成可以送出的行動。"""
        steps, at = [], self.st.here
        for i in order:
            if self.kind[i] not in ("pick", "drop"):
                continue
            steps += self.g.path_steps(at, self.nodes[i])
            steps.append(pickup(self.task[i]["id"]) if self.kind[i] == "pick" else dropoff(self.task[i]["id"]))
            at = self.nodes[i]
        return steps


class Game:
    def __init__(self, url: str, token: str | None = None, timeout: float = 10.0):
        self.url = url.rstrip("/")
        self.token = token
        self.timeout = timeout
        self.session = requests.Session()

        self.session.headers.update({"ngrok-skip-browser-warning": "1"})

    def _req(self, method: str, path: str, **kw):
        r = self.session.request(method, self.url + path, timeout=self.timeout, **kw)
        try:
            data = r.json()
        except ValueError:
            raise ApiError(f"{r.status_code}: {r.text[:200]}")
        if r.status_code >= 400 or data.get("ok") is False:
            raise ApiError(data.get("error") or f"HTTP {r.status_code}")
        return data

    def join(self, name: str, retries: int = 30, quiet: bool = False, role: str = "") -> str:
        for i in range(retries):
            try:
                data = self._req("POST", "/join", json={"name": name, "role": role})
                break
            except requests.RequestException:
                if i == retries - 1:
                    raise
                print("連不上伺服器，2 秒後重試…")
                time.sleep(2)
        self.token = data["token"]
        if not quiet:
            print(f"已加入：{name}（player_id={data['player_id']}）\ntoken = {self.token}  ← 斷線重連時用 Game(url, token=...)")
        return self.token

    def state(self) -> State:
        return State(self._req("GET", "/state", params={"token": self.token or ""}))

    def submit(self, steps: list[dict], append: bool = False, start_node: int = -1):
        return self._req("POST", "/plan", json={"token": self.token, "steps": steps, "append": append,
                                                "start_node": int(start_node)})

    def admin_start(self, key: str = "admin"):
        return self._req("POST", "/admin/start", json={"key": key})

    def admin_reset(self, key: str = "admin"):
        return self._req("POST", "/admin/reset", json={"key": key})

    def results(self) -> dict:
        return self._req("GET", "/results")

    def wait_until_running(self, poll: float = 1.0, after_match: int | None = None) -> State:
        printed = False
        while True:
            st = self.state()
            if st.status == "running" and (after_match is None or st.raw.get("match_id") != after_match):
                return st
            if not printed:
                print("等待比賽開始…")
                printed = True
            time.sleep(poll)

    def run(self, policy: Callable[[State], list[dict] | None], interval: float = 1.0,
            verbose: bool = True):
        self.wait_until_running()
        last_print = 0.0
        while True:
            t0 = time.time()
            try:
                st = self.state()
            except (requests.RequestException, ApiError) as e:
                print("連線錯誤，重試中：", e)
                time.sleep(2)
                continue
            if st.status == "ended":
                print(f"比賽結束：{st.raw['end_reason']}　你的分數：{st.you['score']}")
                return st
            if st.you["state"] == "eliminated":
                print("你已被淘汰（分數保留），等待比賽結束…")
                time.sleep(5)
                continue
            if verbose and st.you["last_error"]:
                print("⚠", st.you["last_error"])
            steps = policy(st)
            if steps is not None and st.you["state"] == "active":
                try:
                    self.submit(steps, start_node=st.here)
                except ApiError as e:
                    if verbose:
                        print("送出失敗：", e)
            if verbose and time.time() - last_print > 10:
                print(st)
                last_print = time.time()
            # 抓狀態、算策略、送計畫已經花掉的時間要扣掉，不然連線慢的時候每輪會多等
            time.sleep(max(0.0, interval - (time.time() - t0)))


def load_map(src):
    if src is None or isinstance(src, dict):
        return src
    src = str(src)
    if src.startswith("http"):
        data = requests.get(src.rstrip("/") + "/map", timeout=10).json()
        if data.get("ok") is False:
            raise ApiError(data.get("error", "伺服器沒有提供自訂地圖"))
        return data
    import json
    return json.load(open(src, encoding="utf-8"))

def test_local(make_policy: Callable[[], Callable], seeds=(1, 2, 3), name: str = "我", role: str = "",
               classmates: int = 11, show: bool = True, map=None) -> list:
    from apocalypse_local import LocalGame
    map_data = load_map(map)
    games = []
    for seed in seeds:
        g = LocalGame(seed=seed, name=name, role=role, classmates=classmates, map_data=map_data)
        try:
            g.run(make_policy())
        except Exception:
            import traceback
            print(f"種子 {seed}：你的程式在第 {g.sim.time:.0f} 秒出錯 ↓")
            traceback.print_exc()
            return games
        r = g.result()
        games.append(g)
        if g.sim.cfg["escape"]:
            t = int(g.sim.time_left())
            city = (f"✅ 直升機在比賽還剩 {t // 60}:{t % 60:02d} 時起飛，{r['escaped_count']}/{r['players']} 人逃出" if r["class_won"]
                    else f"❌ {g.sim.end_reason}")
            mine = f"運零件 {r['parts']}、救援 {r['rescued']}　倒地 {r['downs']} 次　{'搭上直升機' if r['escaped'] else '沒搭上'}"
        else:
            city = "✅ 全班撐過了" if r["class_won"] else f"❌ 城市在 {int(r['ended_at']) // 60}:{int(r['ended_at']) % 60:02d} 淪陷"
            mine = (f"壓低感染 {r['city_reduced']:.1f}%　配送 {r['delivered']}、救援 {r['rescued']}　倒地 {r['downs']} 次"
                    f"　{'撤離成功' if r['alive'] else '沒有撤離'}")
        print(f"種子 {seed}：{city}　｜　你 {r['score']:4d} 分（第 {r['rank']}/{r['players']} 名，同學平均 {r['class_avg']:.0f}）　{mine}")
        if g.errors:
            print(f"　　⚠ 有 {len(g.errors)} 個步驟被略過，例如：{g.errors[0]}")
    if len(games) > 1:
        rs = [g.result() for g in games]
        print(f"全班成功 {sum(r['class_won'] for r in rs)}/{len(rs)} 場；你平均 {sum(r['score'] for r in rs) / len(rs):.0f} 分")
    if show and games:
        replay(games[-1])
    return games

def play(make_policy: Callable[[], Callable], url: str, name: str, role: str = ""):
    import json, os
    game, store = Game(url), "apocalypse_token.json"
    tokens = {}
    if os.path.exists(store):
        try:
            tokens = json.load(open(store, encoding="utf-8"))
        except ValueError:
            pass
    key = f"{url.rstrip('/')}|{name}"
    game.token = tokens.get(key)
    try:
        if game.token and game.state().you.get("id", -1) < 0:
            game.token = None
    except (requests.RequestException, ApiError):
        pass
    while game.token is None:
        try:
            game.join(name, quiet=True, role=role)
            tokens[key] = game.token
            json.dump(tokens, open(store, "w", encoding="utf-8"))
        except ApiError as e:
            print("還不能加入：", e, "（5 秒後再試）")
            time.sleep(5)
    print(f"✅ {name} 已連上，大螢幕上找找你的角色！（按 ■ 停止；改完程式重新執行會接回同一個角色）")
    last = None
    try:
        while True:
            try:
                st = game.wait_until_running(after_match=last)
            except (requests.RequestException, ApiError) as e:
                print("連線中斷，重試中…", e)
                time.sleep(3)
                continue
            last = st.raw.get("match_id")
            print(f"▶ 比賽開始（種子 {st.raw.get('seed')}）")
            final = game.run(make_policy(), verbose=True)
            won = ("✅ 成功！" if final.raw.get("class_won") else "❌ 失敗") if final.raw.get("coop") else ""
            print(f"■ 本場結束：{won} 你 {final.you['score']} 分，等待下一場…")
    except KeyboardInterrupt:
        print("已停止送出計畫（角色會把目前的計畫走完）。改好程式後重新執行這一格即可。")

_TYPE_STYLE = {"shelter": ("#4fc3f7", "s", "避難所"), "hospital": ("#f06292", "P", "醫院"),
               "supply": ("#ffd54f", "D", "補給站"), "factory": ("#a1887f", "s", "工廠"), "generator": ("#26c6da", "p", "發電機")}

def draw(st: State, ax=None, title: str | None = None):
    import matplotlib.pyplot as plt
    show = ax is None
    if ax is None:
        _, ax = plt.subplots(figsize=(10, 7))
    ax.clear()
    for e in st.edges:
        a, b = st.nodes[e["a"]], st.nodes[e["b"]]
        ax.plot([a["x"], b["x"]], [a["y"], b["y"]], color="red" if e["blocked"] else ("#8d6e63" if e["bridge"] else "#d0d0d0"),
                lw=3 if e["bridge"] else 2, ls=":" if e["blocked"] else "-", zorder=1)
    for n in st.nodes.values():
        if n["infection"] > 0.05:
            ax.scatter(n["x"], n["y"], s=700, color=(0.9, 0.1, 0.1, min(0.35, n["infection"] * 0.35)), lw=0, zorder=0)
        if n["type"] in _TYPE_STYLE:
            c, m, _ = _TYPE_STYLE[n["type"]]
            if n["type"] in ("factory", "generator") and not n.get("powered", True):
                c = "#9e9e9e"
            ax.scatter(n["x"], n["y"], s=170, color=c, marker=m, edgecolor="k", zorder=2)
        ax.annotate(str(n["id"]), (n["x"], n["y"]), xytext=(5, -10), textcoords="offset points", fontsize=7, color="#777")
    mine = set(st.you.get("cargo", []))
    for t in st.tasks.values():
        if t["status"] not in ("open", "announced") and t["id"] not in mine:
            continue
        o = st.nodes[t["origin"]]
        c = "#43a047" if t["type"] == "rescue" else ("#fbc02d" if t.get("batch", -1) >= 0 else "#fb8c00")
        if t["type"] == "fuel":
            c = "#e53935"
        ax.scatter(o["x"] + 0.7, o["y"] + 0.7, marker="^" if t["type"] == "rescue" else "s", s=45, color=c,
                   alpha=1 if t["status"] == "open" else 0.35, edgecolor="k", lw=0.5, zorder=3)
    for t in (st.tasks[i] for i in mine if i in st.tasks):
        d = st.nodes[t["dest"]]
        ax.scatter(d["x"], d["y"], marker="x", s=120, color="k", zorder=3)
    for z in st.zombies:
        ax.scatter(z["x"], z["y"], s=35, color="#c62828" if z["mode"] == "chase" else "#558b2f", marker="o", zorder=4)
    e = st.escape
    if e.get("enabled"):
        ex = st.nodes[e["extraction"]]
        ax.scatter(ex["x"], ex["y"], marker="*", s=700, color="#ffd54f", edgecolor="k", zorder=4)
        ax.annotate("撤離點", (ex["x"], ex["y"]), xytext=(0, -22), textcoords="offset points", ha="center", fontsize=9, zorder=6)
        ax.add_patch(plt.Circle((e["zone_x"], e["zone_y"]), e["zone_radius"], fill=False, ls="--", lw=2, color="#e53935", zorder=1))
    for p in st.players:
        if p["state"] == "eliminated":
            continue
        me = p["id"] == st.you.get("id")
        ax.scatter(p["x"], p["y"], s=300 if me else 90, color=p["color"], edgecolor="k", lw=2.5 if me else 0.8, zorder=5,
                   alpha=0.5 if p["state"] == "downed" else 1)
        if me:
            ax.annotate(f"{p['name']} {p['score']}", (p["x"], p["y"]), xytext=(0, 12), textcoords="offset points",
                        ha="center", fontsize=10, fontweight="bold", zorder=6)
    y = st.you
    if e.get("enabled"):
        if e["depart_at"] > 0:
            goal = f"直升機 {max(0, int(e['depart_at'] - st.time))} 秒後起飛"
        elif e.get("ready_at", -1) >= 0:
            goal = f"油桶 {e['fuel']}/{e['fuel_required']}（安全區外倒地無人救援將無法復活）"
        else:
            goal = f"零件 {e['parts']}/{e['parts_required']}"
    else:
        goal = f"城市感染 {st.city_infection:.0f}%"
    ax.set_xlim(min(n["x"] for n in st.nodes.values()) - 3, max(n["x"] for n in st.nodes.values()) + 3)
    ax.set_ylim(min(n["y"] for n in st.nodes.values()) - 3, max(n["y"] for n in st.nodes.values()) + 3)
    ax.set_title(title or f"{st.time:4.0f}s / {st.raw['duration']:.0f}s　{goal}　｜　你：{y.get('score', 0)} 分　"
                          f"生命 {y.get('hp', 0):.0f}", fontsize=11)
    ax.set_aspect("equal"); ax.set_xticks([]); ax.set_yticks([])
    if show:
        plt.show()

_REPLAY_HTML = """
<div id="RID" style="font-family:sans-serif;background:#141821;color:#e8e8e8;padding:10px;border-radius:10px;width:780px">
<canvas width="760" height="520" style="background:#1d2230;border-radius:8px;display:block"></canvas>
<div style="display:flex;align-items:center;gap:10px;margin-top:8px">
<button class="pp" style="width:60px;font-size:15px">⏸</button>
<input class="sl" type="range" min="0" value="0" style="flex:1">
<span class="tm" style="width:110px;text-align:right"></span>
<select class="sp"><option value="1">1x</option><option value="2">2x</option><option value="4" selected>4x</option><option value="8">8x</option><option value="16">16x</option></select>
</div>
<div class="hd" style="margin-top:6px;font-size:14px"></div>
</div>
<script>
(function(){
const D = DATA, ME = MEID;
const root = document.getElementById("RID"), cv = root.querySelector("canvas"), cx = cv.getContext("2d");
const H = D.header, F = D.frames, every = H.every, N = H.nodes;
const xs = N.map(n => n[0]), ys = N.map(n => n[1]);
const x0 = Math.min(...xs) - 1.5, x1 = Math.max(...xs) + 1.5, y0 = Math.min(...ys) - 1.5, y1 = Math.max(...ys) + 1.5;
const sc = Math.min(760 / (x1 - x0), 520 / (y1 - y0));
const ox = (760 - (x1 - x0) * sc) / 2, oy = (520 - (y1 - y0) * sc) / 2;
const X = x => ox + (x - x0) * sc, Y = y => 520 - (oy + (y - y0) * sc);
const sl = root.querySelector(".sl"), pp = root.querySelector(".pp"), sp = root.querySelector(".sp");
const tm = root.querySelector(".tm"), hd = root.querySelector(".hd");
sl.max = (F.length - 1) * 100;
let T = 0, playing = true, last = null;
const FAC = {shelter: "#ffd54f", hospital: "#f06292", supply: "#ffeb3b", factory: "#a1887f", generator: "#26c6da"};
const TASK = {p: "#ff9800", r: "#66bb6a", f: "#e53935", d: "#ff9800"};
const clock = t => { t = Math.max(0, t); return Math.floor(t / 60) + ":" + String(Math.floor(t % 60)).padStart(2, "0"); };
function draw(fi) {
  const i = Math.min(F.length - 1, Math.floor(fi)), f = F[i], g = F[Math.min(F.length - 1, i + 1)], a = fi - i;
  cx.clearRect(0, 0, 760, 520);
  const ex = N[H.extraction];
  cx.setLineDash([6, 6]); cx.strokeStyle = "rgba(255,80,80,0.7)"; cx.lineWidth = 2;
  cx.beginPath(); cx.arc(X(ex[0]), Y(ex[1]), f.z * sc, 0, 7); cx.stroke(); cx.setLineDash([]);
  const blocked = new Set(f.b);
  H.edges.forEach((e, k) => {
    const A = N[e[0]], B = N[e[1]];
    cx.strokeStyle = blocked.has(k) ? "#e53935" : e[2] ? "#8d6e63" : "#4a5266";
    cx.lineWidth = e[2] ? 4 : 2; cx.setLineDash(blocked.has(k) ? [4, 4] : []);
    cx.beginPath(); cx.moveTo(X(A[0]), Y(A[1])); cx.lineTo(X(B[0]), Y(B[1])); cx.stroke();
  });
  cx.setLineDash([]);
  const on = new Set(f.g);
  N.forEach((n, k) => {
    if (n[2] === "road") return;
    let c = FAC[n[2]] || "#fff";
    if (n[2] === "generator" && !on.has(k)) c = "#666";
    cx.fillStyle = c; const r = k === H.extraction ? 9 : 6;
    cx.fillRect(X(n[0]) - r, Y(n[1]) - r, r * 2, r * 2);
  });
  const piles = {};
  f.k.forEach(t => {
    const c = piles[t[0]] = (piles[t[0]] || 0) + 1, n = N[t[0]];
    cx.globalAlpha = t[2] ? 0.35 : 1; cx.fillStyle = TASK[t[1]] || "#fff";
    cx.fillRect(X(n[0]) + 6 + ((c - 1) % 4) * 6, Y(n[1]) - 12 - Math.floor((c - 1) / 4) * 6, 5, 5);
  });
  cx.globalAlpha = 1;
  f.i.forEach(e => {
    const n = N[e[0]], r = 12 + 3 * Math.sin(T / 20);
    cx.strokeStyle = "#ffab40"; cx.lineWidth = 2; cx.beginPath(); cx.arc(X(n[0]), Y(n[1]), r, 0, 7); cx.stroke();
  });
  f.zb.forEach(z => { cx.fillStyle = z[2] ? "#ff5252" : "#7cb342"; cx.beginPath(); cx.arc(X(z[0]), Y(z[1]), 3, 0, 7); cx.fill(); });
  const nxt = {}; g.p.forEach(p => nxt[p[0]] = p);
  let me = null;
  f.p.forEach(p => {
    if (p[3] === 2) return;
    const q = nxt[p[0]] || p, px = p[1] + (q[1] - p[1]) * a, py = p[2] + (q[2] - p[2]) * a;
    const info = H.players[p[0]] || ["?", "#fff"], mine = p[0] === ME;
    if (mine) me = p;
    cx.fillStyle = p[3] === 1 ? "#777" : info[1];
    cx.beginPath(); cx.arc(X(px), Y(py), mine ? 8 : 5, 0, 7); cx.fill();
    if (mine) { cx.strokeStyle = "#fff"; cx.lineWidth = 2.5; cx.stroke(); cx.fillStyle = "#fff"; cx.font = "13px sans-serif"; cx.fillText(info[0], X(px) + 10, Y(py) - 8); }
  });
  const goal = f.depart >= 0 ? "起飛倒數 " + clock(f.depart - f.t) : f.ready >= 0 ? "油桶 " + f.fuel + "/" + f.freq : "零件 " + f.parts + "/" + f.preq;
  hd.textContent = goal + (me ? "　｜　你：" + me[4] + " 分　生命 " + me[5] : "");
  tm.textContent = clock(f.t) + " / " + clock(H.duration);
}
function tick(now) {
  if (last !== null && playing) { T += (now - last) / 1000 / every * sp.value; if (T >= F.length - 1) { T = F.length - 1; playing = false; pp.textContent = "▶"; } sl.value = T * 100; }
  last = now; draw(T); requestAnimationFrame(tick);
}
pp.onclick = () => { if (T >= F.length - 1) T = 0; playing = !playing; pp.textContent = playing ? "⏸" : "▶"; };
sl.oninput = () => { T = sl.value / 100; };
requestAnimationFrame(tick);
})();
</script>
"""


def replay(game, name: str | None = None):
    import json, random
    from IPython.display import HTML, display
    if isinstance(game, dict):
        data = game
    else:
        data = game.replay_data()
        name = name or game.me.name
    if not data.get("frames"):
        print("沒有可以重播的畫面")
        return
    players = data["header"]["players"]
    me = next((int(k) for k, v in players.items() if name and v[0] == name), -1)
    if name and me < 0:
        print(f"重播裡找不到「{name}」，照樣播放（名字要和加入時一樣）")
    rid = "rp" + str(random.randrange(10 ** 9))
    html = (_REPLAY_HTML.replace("RID", rid).replace("MEID", str(me))
            .replace("DATA", json.dumps(data, ensure_ascii=False, separators=(",", ":"))))
    display(HTML(html))


def replay_match(url: str, name: str | None = None):
    data = Game(url, timeout=120)._req("GET", "/replay")
    replay(data, name)
    return data

def benchmark(url: str, make_policy: Callable[[], Callable], matches: int = 8, name: str = "Bench",
              token: str | None = None, interval: float = 1.0) -> list[dict]:
    game = Game(url, token=token)
    if token is None:
        game.join(name)
    me_id = game.state().you.get("id", -1)
    rows, last = [], None
    for k in range(matches):
        st = game.wait_until_running(after_match=last)
        last = st.raw.get("match_id")
        late = st.time > 10
        game.run(make_policy(), interval=interval, verbose=False)
        res = {}
        for _ in range(20):
            res = game.results()
            if res.get("match_id") == last:
                break
            time.sleep(0.5)
        ranking = res.get("ranking", [])
        me = next((r for r in ranking if r["player_id"] == me_id), None)
        if me is None:
            print(f"第 {k + 1} 場：找不到結算")
            continue
        row = dict(match=k + 1, seed=res["seed"], score=me["score"], rank=me["rank"], players=len(ranking),
                   escaped=me.get("escaped", False), late=late)
        rows.append(row)
        print(f"第 {k + 1:2d} 場 種子 {row['seed']:3d}：{row['score']:5d} 分 第 {row['rank']}/{row['players']} 名  "
              f"{'搭上直升機' if row['escaped'] else '沒搭上'}{'  （中途加入，不計）' if late else ''}")
    counted = [r for r in rows if not r["late"]]
    if counted:
        avg = sum(r["score"] for r in counted) / len(counted)
        rank = sum(r["rank"] for r in counted) / len(counted)
        print(f"\n總結（{len(counted)} 場）：平均 {avg:.0f} 分，平均第 {rank:.1f} 名")
    return rows
