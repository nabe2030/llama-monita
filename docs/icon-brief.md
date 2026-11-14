# LlamaMonita アイコン制作仕様 / Icon brief

他のデザイナーや画像生成 AI に渡すための仕様です。下の「依頼文」をそのまま貼って使えます。

## 1. 何のアイコンか

- **アプリ名**: LlamaMonita (「Monita」は日本語の「モニタ」)
- **内容**: llama.cpp (llama-server) と GPU の状態を見る、Windows 11 用のデスクトップモニター。nvtop の Windows 版のような位置づけ
- **画面の特徴**: 濃い紺色の背景に、青い面グラフ (GPU 使用率) と緑の折れ線 (生成速度) が重なる
- **使われる場所**: タスクバー、スタートメニュー、ウィンドウのタイトルバー、exe ファイル、GitHub の README

## 2. コンセプト

「ラマ」と「計測・グラフ」の 2 つが、小さいサイズでも一目でわかること。

| 案 | 内容 |
|---|---|
| A (第一候補) | ラマの横顔のシルエット。首から背中にかけての線が、そのまま折れ線グラフ (または心電図のような波形) になっている |
| B | 角の丸い濃色のタイルの中に、ラマの耳と顔の輪郭。下半分に青の面グラフと緑の線 |
| C | 丸いメーター (ゲージ) の中にラマの顔。針やメモリで「計器」を表す |

3 案すべてでなくて構いません。A を中心に 2〜3 バリエーションを希望します。

## 3. デザインの条件

- **形**: 正方形のキャンバス。背景は透明、またはアイコン自体が角丸のタイル (角の半径はキャンバスの約 22%)
- **色** (アプリの配色に合わせる)

| 用途 | 色 |
|---|---|
| 背景・タイル | `#0F1115` 〜 `#181B22` (濃い紺) |
| 主役の線・面 (GPU) | `#60A5FA` (青) |
| アクセント (生成速度) | `#4ADE80` (緑) |
| 明るい部分・ラマの輪郭 | `#E6E8EC` (オフホワイト) |

- **色数は 3〜4 色まで**。グラデーションは使っても控えめに
- **文字は入れない** (「LM」などのイニシャルも不可)
- **16×16 ピクセルで判別できること**。細い線、小さな目鼻、細かい毛並みは避ける。16px と 24px 用には、線を太くし要素を減らした簡略版を別に用意する
- **明るいタスクバーでも暗いタスクバーでも沈まないこと**。透明背景の場合は、輪郭に十分な明度差を付ける
- **スタイル**: フラットで幾何学的。Windows 11 の標準アイコンに並べて違和感のない、すっきりした造形。写実的・3D・マスコット調の描き込みは避ける
- **向き**: ラマは右向き (グラフが左から右へ進むのに合わせる)

## 4. 使ってはいけないもの

- llama.cpp や ggml の公式ロゴ、Meta の Llama のロゴ、Ollama など既存のラマのマスコットに似せた造形
- NVIDIA、AMD、Intel、Windows のロゴや、それを連想させる形・配色
- 他者の著作物の流用。**完全なオリジナル**であること (MIT ライセンスのリポジトリで再配布するため)

## 5. 納品物

| ファイル | 内容 |
|---|---|
| `llamamonita.svg` | ベクターの元データ (アウトライン化済み) |
| `llamamonita-1024.png` | 1024×1024、背景透明 |
| `llamamonita-256.png`、`-64.png`、`-48.png`、`-40.png`、`-32.png` | 標準版を各サイズで書き出したもの |
| `llamamonita-24.png`、`-20.png`、`-16.png` | 小サイズ用の簡略版。ピクセルの境界に合わせて調整する |
| `llamamonita.ico` (可能なら) | 上記の 16 / 20 / 24 / 32 / 40 / 48 / 64 / 256 を 1 つにまとめたもの。無ければこちらで作る |

PNG はすべて 32 bit (アルファ付き)、sRGB。

## 6. 確認の観点

1. 16px に縮めてもラマとグラフのどちらかが読み取れるか
2. 白い背景と黒い背景の両方に置いて見えるか
3. アプリの画面 (濃紺に青と緑) の隣に置いて、同じ製品に見えるか
4. 既存のロゴと紛らわしくないか

---

## 依頼文 (日本語)

> Windows 11 用デスクトップアプリ「LlamaMonita」のアプリアイコンを作ってください。llama.cpp という AI 実行ソフトと GPU の状態をグラフで監視するツールです。
>
> モチーフ: 右向きのラマの横顔のシルエット。首から背中にかけての輪郭線が、そのまま右上がりの折れ線グラフ (または心電図のような波形) になっている。「ラマ」と「計測」が一目でわかること。
>
> スタイル: フラットで幾何学的、シンプル。Windows 11 の標準アイコンと並べて違和感のない造形。写実的・3D・マスコット調にはしない。文字は入れない。
>
> 色: 濃い紺 (#0F1115) の角丸タイルの上に、青 (#60A5FA) と緑 (#4ADE80) の線、輪郭はオフホワイト (#E6E8EC)。色数は 3〜4 色。
>
> 条件: 正方形、1024×1024、角丸タイル以外の背景は透明。16×16 ピクセルに縮めても判別できるよう、細い線や細かい描き込みは避ける。既存のロゴやマスコット (llama.cpp、Meta Llama、Ollama、NVIDIA、AMD など) に似せない、完全なオリジナルにすること。
>
> 2〜3 案のバリエーションと、16px・24px 用に要素を減らした簡略版もお願いします。

## Prompt (English)

> Design an app icon for "LlamaMonita", a Windows 11 desktop app that monitors llama.cpp (a local AI runtime) and GPU activity with live graphs.
>
> Motif: the side-profile silhouette of a llama facing right, where the contour running from its neck along its back turns into a rising line chart (or a heartbeat-style pulse). "Llama" and "measurement" should both read at a glance.
>
> Style: flat, geometric, minimal; it should sit comfortably next to stock Windows 11 icons. Not photorealistic, not 3D, not a cartoon mascot. No text or letters.
>
> Colors: a dark navy rounded-square tile (#0F1115) with a blue line or area (#60A5FA) and a green accent line (#4ADE80); the llama outline in off-white (#E6E8EC). Three to four colors in total.
>
> Constraints: square 1024×1024 canvas, transparent outside the rounded tile (corner radius about 22% of the canvas). It must stay recognizable at 16×16 pixels, so avoid thin strokes and fine detail. It must be fully original and must not resemble existing logos or mascots (llama.cpp, Meta Llama, Ollama, NVIDIA, AMD, Microsoft).
>
> Please provide two or three variations, plus a simplified version with fewer elements for the 16 px and 24 px sizes.
