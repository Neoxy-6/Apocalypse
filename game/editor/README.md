# 自訂地圖

不想用隨機地圖，可以自己畫一張。路口、道路、河、橋、建築都自己放。

## 畫地圖

用瀏覽器打開 `map_editor.html`（直接雙擊）。

| 工具 | 按鍵 | 用法 |
|---|---|---|
| 選取 / 移動 | V | 點路口選它，拖著可以移動 |
| 放路口 | N | 點空白處放一個路口 |
| 連道路 | E | 先點一個路口再點另一個，就會連起來。兩個已經連著的話會拆掉。點空白處會直接放新路口接上去 |
| 設橋 | B | 點一條路，變成橋，再點一次變回來 |
| 畫河流 | R | 一路點下去，按 Enter 或雙擊結束 |
| 刪除 | D | 點到什麼刪什麼 |

選到路口後，按 1 到 7 換類型（道路、撤離點、醫院、補給站、工廠、發電機、軍械庫），按 I 設成一開始就感染，按 Delete 刪掉。

滾輪縮放，按住右鍵拖曳移動畫面，Ctrl+Z 復原。懶得從零開始的話，可以按「從格子開始…」先生一張方格再改。

右邊的「檢查」：

- 紅色要改，不然遊戲不會用這張圖：路口少於 4 個、撤離點不是剛好 1 個、有路口走不到
- 黃色只是提醒，例如沒有醫院、路穿過河卻沒設成橋

兩邊的風格在右邊選。拿掉所有橋之後，跟 0 號路口連在一起的是 A 區，另一邊是 B 區。

沒標感染點的話，遊戲會自己挑離撤離點最遠的 2 個路口。

畫完按「匯出 JSON」。要改舊的地圖就按「匯入 JSON」，或直接把檔案拖進畫面。`game/unity/Assets/StreamingAssets/maps/example.json` 是一張範例。

## 開遊戲

```bash
Apocalypse.exe -map test.json
```

或在 `StreamingAssets/config.json` 寫 `"mapFile": "test.json"`。

遊戲會照這個順序找檔案：

1. 下指令的資料夾（也可以寫完整路徑，或像 `mapset/TEST.json` 這樣的相對路徑）
2. 打包版的 `Apocalypse_Data/StreamingAssets/maps/`
3. 專案的 `game/unity/Assets/StreamingAssets/maps/`

所以地圖丟進專案的 maps 資料夾就好，不用重新打包。

找不到檔案或地圖有問題，遊戲會改用隨機地圖，原因寫在 log 裡。log 也會寫用了哪個檔案。

`-seed` 還是有用，事件、零件、民眾出現的方式由它決定，地圖則照檔案。

## 練習

伺服器用自訂地圖時，在 Colab 加上 `map=SERVER_URL`，就會用同一張地圖練習：

```python
games = test_local(my_make_policy, seeds=[1, 2, 3], name=MY_NAME, map=SERVER_URL)
```

`map=` 也可以填 JSON 檔的路徑。

## 檔案格式

```json
{
  "name": "我的地圖",
  "nodes": [ { "x": 0, "y": 0, "type": "extraction" }, { "x": 6, "y": 0, "type": "road", "infected": true } ],
  "edges": [ { "a": 0, "b": 1 }, { "a": 1, "b": 2, "bridge": true } ],
  "river": [ { "x": 9, "y": -3 }, { "x": 9, "y": 9 } ],
  "themes": ["city", "ruins"]
}
```

- 一格大約是 6
- `type` 可以是 `road`、`extraction`、`hospital`、`supply`、`factory`、`generator`、`armory`
- `edges` 裡的 `a`、`b` 是路口在 `nodes` 裡的順序，從 0 開始
- `themes` 是 A 區和 B 區的風格：`jungle` 雨林、`city` 城市、`ruins` 廢墟、`town` 小城鎮
