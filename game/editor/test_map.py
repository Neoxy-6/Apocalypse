"""
要跑要把整個 repo copy 下來喔
"""
import argparse
import collections
import contextlib
import io
import json
import multiprocessing as mp
import os
import statistics
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "unity", "Assets", "StreamingAssets", "sdk"))
from apocalypse_local import LocalGame, classmate_policy


def one(args):
    seed, map_data = args
    g = LocalGame(seed=seed, classmates=11, frame_every=1e9, map_data=map_data)
    with contextlib.redirect_stdout(io.StringIO()):
        g.run(classmate_policy())
    s = g.sim
    return dict(seed=seed, won=bool(s.class_won), escaped=sum(p.escaped for p in s.players),
                ready=s.ready_at, reason=s.end_reason)


def run(seeds, map_data, jobs):
    with mp.Pool(jobs) as pool:
        return pool.map(one, [(s, map_data) for s in seeds])


def why(reason):
    if "倒下" in reason:
        return "全員倒下"
    if "沒修好" in reason:
        return "零件湊不齊"
    if "油" in reason:
        return "油不夠"
    return reason.split("！")[0]


def summary(rows):
    won = sum(r["won"] for r in rows)
    ready = [r["ready"] for r in rows if r["ready"] >= 0]
    fails = collections.Counter(why(r["reason"]) for r in rows if not r["won"])
    return won, ready, fails


def parse_seeds(text):
    out = []
    for part in text.split(","):
        a, _, b = part.partition("-")
        out += list(range(int(a), int(b or a) + 1))
    return out


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description="測自訂地圖的難度")
    ap.add_argument("map", help="地圖 JSON 檔")
    ap.add_argument("--seeds", default="1-20", help="例如 1-20 或 1,5,9")
    ap.add_argument("--jobs", type=int, default=max(1, (os.cpu_count() or 2) - 1), help="同時跑幾場")
    ap.add_argument("--no-compare", action="store_true", help="不跑隨機地圖對照")
    args = ap.parse_args()

    map_data = json.load(open(args.map, encoding="utf-8"))
    seeds = parse_seeds(args.seeds)
    print(f"地圖：{map_data.get('name') or args.map}，{len(map_data['nodes'])} 個路口，跑 {len(seeds)} 個 seed…", flush=True)
    mine = run(seeds, map_data, args.jobs)
    won, ready, fails = summary(mine)
    print(f"\n你的地圖：全班贏 {won}/{len(seeds)}（{won / len(seeds):.0%}）")
    if ready:
        print(f"  直升機平均第 {statistics.mean(ready):.0f} 秒修好（{len(ready)}/{len(seeds)} 場有修好）")
    if fails:
        print("  輸的原因：" + "、".join(f"{k} {v} 場" for k, v in fails.most_common()))
    print("  每個 seed：" + " ".join(f"{r['seed']}{'✅' if r['won'] else '❌'}" for r in sorted(mine, key=lambda r: r["seed"])))

    if not args.no_compare:
        print("\n跑隨機地圖對照…", flush=True)
        base = run(seeds, None, args.jobs)
        bwon, bready, _ = summary(base)
        print(f"隨機地圖：全班贏 {bwon}/{len(seeds)}（{bwon / len(seeds):.0%}）" +
              (f"，直升機平均第 {statistics.mean(bready):.0f} 秒修好" if bready else ""))
        diff = won - bwon
        verdict = "差不多" if abs(diff) <= len(seeds) * 0.1 else ("比隨機地圖簡單" if diff > 0 else "比隨機地圖難")
        print(f"\n結論：{verdict}（同一批 seed、同一班 bot，你的地圖贏 {won} 場，隨機地圖贏 {bwon} 場）")
    rate = won / len(seeds)
    print("建議：" + ("可以上課用" if rate >= 0.6 else "偏難，可以把工廠拉近撤離點、撤離點放中間一點" if rate >= 0.3
                    else "太難，先看右邊編輯器的黃色提醒"))


if __name__ == "__main__":
    main()
