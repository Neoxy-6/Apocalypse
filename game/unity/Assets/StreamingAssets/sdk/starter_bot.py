from __future__ import annotations

from apocalypse_sdk import CityGraph, State, dropoff, fix, goto, heal, pickup, revive, use_medkit

def make_policy():
    def policy(st: State):
        me = st.you
        g = CityGraph(st)

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
        if st.time < me["next_plan_at"]:
            return None

        for q in st.players:
            if q["state"] == "downed" and q["id"] != me["id"]:
                eta = g.dist(st.here, q["node"]) / me["speed"]
                others = [g.dist(o["node"], q["node"]) for o in st.players
                          if o["state"] == "active" and o["id"] not in (me["id"], q["id"])]
                if eta + 4 < q["downed_timer"] and g.dist(st.here, q["node"]) <= min(others, default=1e9):
                    return [goto(q["node"]), revive(q["id"])]

        cargo = st.my_cargo()
        if cargo:
            return [s for t in cargo for s in (goto(t["dest"]), dropoff(t["id"]))]

        for inc in sorted(st.active_incidents(), key=lambda i: g.dist(st.here, i["node"])):
            eta = g.dist(st.here, inc["node"]) / me["speed"]
            if eta < 25 and eta + 3 < inc["deadline"] - st.time and st.helpers(inc["id"]) < inc["min_people"]:
                return [goto(inc["node"]), fix(inc["id"], 20)]

        claimed = st.claimed_tasks()
        tasks = [t for t in st.open_tasks() if t["id"] not in claimed]

        if st.escape.get("ready_at", -1) >= 0:
            fuel = [t for t in tasks if t["type"] == "fuel"]
            if not fuel:
                return None if st.here == ex else [goto(ex)]
            tasks = fuel
        if not tasks:
            return None

        def cost(t):
            return (g.dist(st.here, t["origin"]) + g.dist(t["origin"], t["dest"])) / max(1, t["reward"])

        t = min(tasks, key=cost)
        return [goto(t["origin"]), pickup(t["id"]), goto(t["dest"]), dropoff(t["id"])]

    return policy
