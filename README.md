# KyoshinShindoPlaceEditor

[简体中文](readme_CN.md) | 日本語

強震モニタ画像の色を取得するピクセル座標を編集する Windows Forms アプリケーションです。
この fork は [ingen084 の原版](https://github.com/ingen084/KyoshinShindoPlaceEditor) 0.0.6.0 を基に、簡体字中国語 UI、新形式 JSON 対応、読み込み時の例外対策、公式 CSV ダウンロード、元に戻す操作、選択点の表示補助を追加しています。UI のメニュー名は以下も中国語表記です。

## この fork の変更点

- UI とエラーメッセージを簡体字中国語化。観測点名・地域名・コードは翻訳しません。
- 強震モニタの背景・地表・地中画像 URL に含まれていた無効な `/new/` を修正。
- `kyoshin-monitor-observation-points` の `intensity-points.json` に対応。旧 PascalCase JSON も引き続き読み書きできます。
- 無効なデータの読み込み失敗時に既存データを保持し、描画で例外が起きてもウィンドウの操作状態を復元。
- K-NET / KiK-net の公式 CSV をアプリ内からダウンロード。現行 11 列と旧 14 列の NIED CSV に対応。
- `Ctrl+Z` による最大 50 操作の取り消し。
- 選択点の黄色い点滅と、原画像で 3×3 ピクセルの安全枠。**表示・注意喚起のみで、重複配置や保存を禁止しません。**

## 起動と基本操作

Windows と .NET Framework 4.7 以降が必要です。配布版の DLL と設定ファイルを EXE と同じディレクトリに置き、`KyoshinShindoPlaceEditor.exe` を起動してください。

1. `文件 → 打开` で対応する点位ファイルを開きます。
2. 右の一覧から観測点を選び、左上の画像を左クリックして画像座標を設定します。
3. 画像上の既存の点を中クリックすると、対応する観測点を選択できます。
4. 一覧の右クリックメニューから休止状態の切り替えや画像座標のクリアができます。
5. `文件 → 保存` または `另存为` で保存します。**変更は自動保存されません。**

画像・地図は右ドラッグで移動できます。画像を表示範囲外まで動かさないよう注意してください。
`刷新图像` は手動更新で、`监测图像来源` から地表・地中を切り替えます。

### 2 つの地図の違い

- **左上**: 352×400 ピクセルの強震モニタ画像。ここで編集する値は画像の色を取得する座標であり、緯度経度ではありません。1x～5x の表示倍率に対応します。
- **左下**: 内蔵の静的な地理地図。選択点の緯度経度を赤い印で示します。地理位置の確認用で、緯度経度の編集、オンライン地図、ホイールズームはありません。印が画面外の場合は右ドラッグで探してください。

### 点滅と 3×3 安全枠

選択点は 500 ミリ秒ごとに黄色く点滅し、安全枠は点滅せず表示されます。
安全枠は原画像の 3×3 ピクセルで、倍率に合わせて拡大されます。
他の点位がなければ青緑、有効な画像座標が枠内にあれば赤になり、下のステータスバーに件数を表示します。休止中の観測点も判定対象です。

判定には実際の取得座標（新 JSON では `center_point + offset`）を使用します。
**枠は参考表示です。重複する場所への移動・編集・保存も可能です。**

### 元に戻す

`编辑 → 撤销` または `Ctrl+Z` で最大 50 操作を戻せます。
座標移動・クリア、休止状態の変更、並べ替え、NIED / EqWatch のインポートに対応し、インポート全体は 1 操作として扱います。
保存後も取り消せますが、ファイルへ反映するには再保存が必要です。
ファイルを正常に開くと履歴を消去し、読み込み失敗時には保持します。表示倍率、選択、ドラッグ、ダウンロードは履歴に入りません。やり直し機能はありません。

## ファイル形式

| 形式 | 開く / 保存 | 注意 |
| --- | --- | --- |
| JSON (`.json`) | 対応 | 新形式と旧 PascalCase 形式 |
| MessagePack (`.mpk`) | 対応 | 原版形式 |
| LZ4 MessagePack (`.mpk.lz4`) | 対応 | 原版形式 |
| エディター独自 CSV (`.csv`) | 対応 | NIED の CSV とは別形式 |
| NIED CSV | 専用インポート | `导入 → NIED 观测点信息` を使用 |
| ProtocolBuffers (`.pbf`) | 未対応 | 保存ダイアログに項目がありますが保存処理は未実装 |
| `.kmop` / `sitepub_all_utf8.csv` | 未対応 | 新形式や Web 用統合 CSV の直接読み込みは未実装 |

### intensity-points.json

小文字のフィールドと `point.center_point` / `point.offset` をそのまま読み込みます。
新形式 JSON を開いて JSON に保存する場合、元の形式、追加フィールド、`sub_region`、未編集の緯度経度の精度を保持します。
移動後の偏差が各軸 -8～7 の範囲なら中心を保持し、範囲外なら新しい中心とゼロ偏差で保存します。
追加情報を保持したい場合は JSON を使ってください。CSV / MessagePack へ変換すると新形式固有の情報は保持されません。

最初は `另存为` で別名の JSON に保存し、内容を確認することを推奨します。

### エディター独自 CSV

ヘッダーなしの以下の列順です。未設定の画像 X / Y は空欄です。原版ライブラリの CSV 処理には欠損値や引用符を含む値の制限があるため、通常は JSON を推奨します。

```text
network(0=unknown,1=KiK-net,2=K-NET),code,suspended(True/False),name,region,latitude,longitude,image_x,image_y,classification_id,prefecture_classification_id,old_latitude,old_longitude
```

## NIED 公式 CSV

1. `导入 → NIED 官方下载` から K-NET と KiK-net をそれぞれダウンロードします。
2. EXE のディレクトリへ `sitepub_knet_sj.csv` と `sitepub_kik_sj.csv` の名前で保存します（既定名）。
3. `导入 → NIED 观测点信息` で 2 ファイルをまとめてインポートします。

取得元は [NIED 観測点一覧](https://www.kyoshin.bosai.go.jp/ja/stationlist/) です。サイトのフォームを通して取得するため、固定の CSV URL を直接開く方式ではありません。ネットワーク処理はバックグラウンドで行い、検証済みの元の CSV バイト列を保存します。失敗した場合は既存ファイルを置換しません。

Shift-JIS の現行 11 列・旧 14 列を読み込み、コードで照合して名称・地理情報・休止状態を更新します。既存の画像座標は維持し、旧地理座標が欠けている場合は既存値を維持します。両ファイルを検証してから更新し、途中で失敗した場合は変更を戻します。

**公式 CSV には画像取得座標がありません。** 新規に登録された点には別途画像座標を設定してください。通常の `文件 → 打开` ではなく専用インポートを使います。サイトの仕様が変わった場合は同じメニューから公式ページを開いてください。

## EqWatch インポート

`v5 Kyoshin Monitor Base Data for EqWatch` 形式の `Kansokuten.dat` をプログラムの**作業ディレクトリ**にコピーし、`导入 → EqWatch 的 Kansokuten.dat` を実行します。EXE のディレクトリを作業ディレクトリとして起動すると分かりやすくなります。

EqWatch は観測点コードを持たないため、ネットワーク・名前・地域で照合します。未一致の点には `_EQW番号` を付けるので、NIED データを先にインポートしてください。名前の表記差（例: `烏海` / `鳥海`）がある場合はコピー側を確認してください。既存の画像座標がある場合は補完モードを選択できます。データの利用条件は EqWatch 作者の指示に従ってください。

## ビルドとテスト

Windows、PowerShell、Visual Studio / Build Tools（.NET デスクトップ用 MSBuild と C# コンパイラー）、原版 0.0.6.0 の配布 DLL が必要です。ターゲットは .NET Framework 4.7 のままです。

```powershell
git clone --recurse-submodules https://github.com/YASJUN/KyoshinShindoPlaceEditor.git
cd KyoshinShindoPlaceEditor
.\Build-Chinese.ps1 -OriginalReleaseDir 'C:\Tools\KyoshinShindoPlaceEditor.0.0.6.0'
```

`OriginalReleaseDir` は原版の DLL がある実際の場所へ変更してください。スクリプトは必要なら NuGet から .NET Framework 4.7 の参照アセンブリを取得し、原版 `KyoshinMonitorLib.dll` を参照してビルドします。依存 DLL の更新や自動デプロイは行いません。従来のライブラリ参照による `MSB3277`（`System.Net.Http`）警告が出る場合があります。

出力は `KyoshinShindoPlaceEditor\bin\Release\KyoshinShindoPlaceEditor.exe` です。利用時は原版配布ディレクトリをバックアップしてから EXE を置き換え、DLL と既存の `KyoshinShindoPlaceEditor.exe.config` を維持してください。ビルド出力全体を配布先へ上書きしないでください。

```powershell
.\tests\Test-Editor.ps1 `
  -ReleaseDir 'C:\Tools\KyoshinShindoPlaceEditor.0.0.6.0' `
  -SamplePath 'C:\Data\intensity-points.json'
# 上記に -Network を追加すると NIED の実ダウンロードも検証
.\tests\Test-ImageResources.ps1
```

サンプル JSON は対応する実データを別途用意してください。テスト用にコピーして使用し、入力ファイル自体は書き換えません。通常の回帰テストは 12 項目、`-Network` 付きは 13 項目です。画像リソースのテストにもネットワーク接続が必要です。

主な追加コードは `ObservationPointJsonDocument.cs`（JSON 互換・検証）、`NiedCsvFile.cs`（取得・解析）、`MainForm.Editing.cs`（履歴）、`MainForm.Selection.cs`（表示補助）、`MainForm.Downloads.cs`（メニュー）です。

## 設定と既知の制限

- 既存の `KyoshinShindoPlaceEditor.exe.config` 内の `AutoLoadFilePath` で起動時の読み込み先を指定できます。絶対・相対パスに対応します。
- リアルタイム画像は自動更新ではありません。PC の時刻とネットワーク、配信側の状態によって取得に失敗する場合があります。
- 原版の `退出` メニューはクリック処理が未実装です。終了にはウィンドウの閉じるボタンを使ってください。
- 緯度経度の編集、地図の自動センタリング、やり直しは追加していません。
- 本 fork の変更は原作者による更新ではありません。不具合はこの fork の Issue に、入力形式・操作手順・エラー内容を添えて報告してください。

## 作者・ライセンス・データ

原作者は **ingen084** です。[LICENSE.md](LICENSE.md) と各依存ライブラリのライセンスを参照してください。この fork は原版のライセンスを置き換えず、プロジェクト全体を MIT として扱いません。

原 README のソース利用に関する記載を維持します:

> あまりにもひどいソースコードが氾濫するのを防ぐため、`KyoshinShindoPlaceEditor`に関しては引用などは原則禁止とさせていただきます。
> どうしても使いたい場合があれば連絡していただければ限定的に許可する場合もあります。

原作者は生成ファイルを利用する際の報告を希望しており、クレジット表記は任意としています。公開アプリへの同梱は要相談とされています。再配布については原 README に「各ライブラリのライセンスに従う」「LICENSE.md を添付する」との記載があります。これらは原版の条件として残し、この fork から追加の許諾を与えるものではありません。

NIED / EqWatch データはそれぞれの利用条件に従ってください。左下の地図は © OpenStreetMap contributors。利用による損害について原作者は責任を負わない旨を記載しています。
