# Cognitive3D Spatial Analytics for Basis VR (`net.minetake.cognitive3d`)

Cognitive3D 空間アナリティクス SDK を Basis VR フレームワークへ統合する拡張パッケージです。
エンタープライズ研修、医療・産業シミュレーション、リテール検証、スタンドアロン VR アプリケーションなどの非 UGC（自社製シーン・ビルド固定）ユースケース向けに最適化されています。

---

## 主な機能

1. **自律的ライフサイクル連携**:
   - `BasisDeviceManagement` の初期化完了時に自動ブートストラップ。
   - `BasisNetworkManagement` のマルチプレイヤー接続と連動し、ルームのホスト・ポートから `LobbyId` を自動設定。同一ルーム内の複数プレイヤーセッションをダッシュボード上で統合分析可能。
   - アプリケーション終了（`OnApplicationQuit`）時およびルーム退室時にバッファデータを自動フラッシュ。

2. **視線（Gaze / Fixation）の透過的ブリッジ**:
   - Basis の `BasisEyeTrackingManager`（OpenXR Eye Gaze、OSC Face Tracking）と直結。
   - アイトラッキング非対応デバイスでは HMD ヘッドカメラの正面ベクトルへ自動フォールバック。

3. **プライバシー＆オプトアウト制御**:
   - PII（個人識別情報）を排除した永続匿名 UUID（`ParticipantId`）によるセッション管理。
   - Basis の設定画面（Developer タブ）に「Cognitive3D Spatial Analytics」セクションを自動注入。
   - ユーザーによる明示的なオプトアウト（Do Not Track）トグルをサポート。オプトアウト時は全イベントがローカルで破棄されます。
   - 音声録音（Speech-to-Text 用）はデフォルトで無効化。

4. **安全な C# API ファサード (`BasisCognitive3DAPI`)**:
   - ゲームプレイやトレーニングシナリオから直接呼べるカスタムイベント送信、Dynamic Object のエンゲージメント（把持・操作）記録 API を提供。

---

## 導入手順

### 1. Cognitive3D Unity SDK の導入
Unity Package Manager（Package Manager > `+` > `Add package from git URL...`）から Cognitive3D SDK を追加します：
```
https://github.com/CognitiveVR/cvr-sdk-unity.git
```
※本パッケージは `COGNITIVE3D_EXISTS` シンボルでガードされているため、SDK 導入前でも Basis の既存コードにコンパイルエラーを発生させません。

### 2. プロジェクトの初期セットアップ
1. Unity メニューから `Cognitive3D > Project Setup` を開きます。
2. Cognitive3D ダッシュボードで発行した **Developer Key** を入力し、**Get from Dashboard** をクリックして Application Key を取得します。
3. `Auto-select XR SDK` を有効にします（Basis の OpenXR サブシステムと連動します）。

### 3. シーンメッシュのアップロード（オーサリング）
1. 対象の Unity シーンを開きます。
2. Unity メニューから `Cognitive3D > Scene Manager` を開きます。
3. 静的ジオメトリとマテリアルを Cognitive3D クラウドへエクスポート＆アップロードします。
   - これにより、ダッシュボード上の WebGL 3D Session Replay やヒートマップでワールド形状が完全再現されます。

### 4. 動的オブジェクト（Dynamic Object）の配置
追跡したい動的オブジェクト（工具、スイッチ、商品等）に以下を設定します：
- **Collider**（`IsTrigger = false`）：視線ヒット判定に必要です。
- **`DynamicObject` コンポーネント**: Feature Builder または直接アタッチします。

---

## C# API の使用例

```csharp
using Net.Minetake.Basis.Cognitive3D;
using System.Collections.Generic;
using UnityEngine;

public class TrainingStepController : MonoBehaviour
{
    [SerializeField] private GameObject fireExtinguisher;

    // ステップ完了イベントの記録
    public void OnStepCompleted(string stepName, float score)
    {
        BasisCognitive3DAPI.SendCustomEvent("StepCompleted", transform.position, new Dictionary<string, object>
        {
            { "StepName", stepName },
            { "Score", score }
        });
    }

    // オブジェクトの操作開始（消火器を掴んだ）
    public void OnExtinguisherGrabbed()
    {
        BasisCognitive3DAPI.BeginEngagement(fireExtinguisher, "Grabbed");
    }

    // オブジェクトの操作終了（消火器を戻した）
    public void OnExtinguisherReleased()
    {
        BasisCognitive3DAPI.EndEngagement(fireExtinguisher);
    }
}
```

---

## ライセンス
MIT License. Developed for Basis VR.
