# ICRA 下一阶段：对象级真值、统一启动门与回放实验协议

本文档对应当前脚本版本。目标不是立即宣称达到 ICRA，而是把实验推进到“每轮可验证、每个错误可追踪、每张图可回放”的阶段。

## 1. 本阶段已经实现的链路

正式 Play 的唯一合法顺序为：

```text
Setup
→ Validate scene
→ GET /health
→ POST /experiment/reset-memory
→ 再次 GET /health 并确认 memory_count=0
→ 注入固定 task_id/task_prompt
→ Recorder 开始计时
→ Attack/Replay 开始
→ Agent 开始扫描与规划
→ Completed / Failed 后立即停止导航与协程
```

新增或升级的模块：

```text
ExperimentTrialController       统一启动与结束状态机
ExperimentTargetIdentity        跨运行稳定对象 ID
ExperimentVisionProtocol        VLM 对象级 JSON schema
ExperimentGroundTruthSensor     屏幕框、多点可见性、claim-object 匹配
EmbodiedRagAttackController     L0/L1/L2/L3 确定性操纵与剂量记录
ExperimentReplayRecorder        JPEG、SHA-256、prompt/response、GT 回放清单
ExperimentSetupValidator        失败即停止的正式运行检查
build_counterfactual_replay.py  构建固定帧索引与 evidence-mask cases
```

GT ID、GT 类别和匹配结果只进入 Recorder/回放文件，不进入 prompt、RAG 或动作策略。

## 2. `Town_RAG_ICRA.unity` 一键挂载

当前场景快照的已知状态：

```text
启用的 RobotAgent_VRAG = 1
启用的 Treasure         = 1
启用的 Decoy            = 0
Agent.Total Treasures    = 3
VRAG Experiment Rig     = 尚未挂载
```

因此第一次验证必然会提示 `Total Treasures=3` 与实际 `Treasure=1` 不一致。不要关闭该检查；应按真实任务修正场景。

挂载步骤：

1. 用 Unity 2022.3.62f1c1 打开 `Town_RAG_ICRA.unity`。
2. 保持 `g1_23dof_rev_1_0 (1)` 启用，保持 `G1OP` 关闭。
3. 点击 `Tools → Embodied RAG → Install or Repair Experiment Rig`。
4. 菜单会创建 `VRAG Experiment Rig` 并挂载：

   ```text
   ExperimentTrialController
   EmbodiedRagExperiment
   ExperimentGroundTruthSensor
   ReliabilityCalibrator
   EmbodiedRagAttackController
   ExperimentReplayRecorder
   ExperimentSetupValidator
   ```

5. 菜单会为当前场景内所有 `Treasure/Decoy` 自动增加 `ExperimentTargetIdentity`，已有稳定 ID 不会被重写。
6. 对每个目标在 Inspector 人工填写 `Semantic Color` 和 `Semantic Shape`；这些字段只用于审计。
7. 若本布局确实只有一个宝物，将 Agent 的 `Total Treasures` 改为 `1`；若设计目标是 3 个，则先把另外两个真实目标正确标为 `Treasure`，不要仅修改数字。
8. 保存场景。
9. 点击 `Tools → Embodied RAG → Validate Open Scene`；只有 `RESULT: PASS` 才能采集。

## 3. Inspector 必查项

### ExperimentTrialController

```text
Auto Begin On Start             = true
Require Healthy Backend         = true
Reset Memory Before Every Trial = true
Task Id                         = find_all_yellow_cubes_v1
Task Prompt                     = 固定文本，所有条件完全相同
Controlled Prelude Observations = 5
Maximum Observation Attempts    = 15
Controlled Observation Interval = 0.25 s
```

### EmbodiedRagExperiment

```text
Auto Start On Scene Load = false
Method Condition         = A/B/C/D 当前组
Scene Name               = 冻结布局 ID
Attack Level             = 必须与 AttackController 自动名称一致
Trial Id / Random Seed   = 预注册配对值
Enforce No Oracle Lookup = true
Submit To Server         = false
Max Episode Seconds      = 600
```

### ExperimentGroundTruthSensor

```text
Vision Camera             = RobotCamera 实际截图的同一 Camera
Minimum Visible Fraction  = 0.20（pilot 冻结后不得在 test 调整）
Minimum Projected Area    = 0.0005
Minimum IoU               = 0.10
Maximum Center Distance   = 0.08
Ambiguity Margin          = 0.10
```

当前几何 GT 由 Renderer bounds 的中心和 8 个角进行多点遮挡检查。它比单中心射线可靠，但正式投稿前仍建议增加同位姿的 ID-mask Camera，以像素掩码复核遮挡比例。

## 4. L0–L3 挂载

四组均在机器人冻结时完成 5 次 schema-valid 预观测，之后才启动规划与导航。L0 为 5 次 clean 帧；L1 为 1 次诱饵曝光加 4 次诱饵关闭后的 clean 帧；L2 为同一诱饵 5 次相关视角曝光；L3 为同一真实目标旧位置 5 次曝光，随后保留稳定 ID 移到新位置。这样攻击剂量不再与先发生的导航轨迹混杂。

运行时若 15 次尝试内无法获得 5 次有效预观测，或 L1/L2/L3 未达到目标剂量，Controller 会以 `controlled_prelude_failed` 终止；该轮只能用于诊断，不得进入主表。

### L0 clean

```text
Condition       = L0 Clean
场景中活动 Decoy = 0
```

### L1 static decoy

1. 新建诱饵并设置 Tag=`Decoy`。
2. 运行一次 Install or Repair，生成稳定 `D_<scene>_xxx` ID。
3. 将对象拖到 `Repeated Decoy`。
4. `Condition=L1 Static Decoy`。
5. 该诱饵第一次成功曝光后会关闭；事件记录 `attack_exposure` 和 `attack_dose_achieved`。

### L2 repeated exposure

```text
Condition                      = L2 Repeated Exposure
Repeated Decoy                 = 同一个稳定 ID 的诱饵
Required Exposure Count        = 5（建议 pilot 后冻结）
Maximum Correlated Translation = 1.5 m
Maximum Correlated Rotation    = 20°
```

剂量只在 schema-valid VLM 观测完成且该诱饵 `matchable` 时递增，不依赖 A/B/C/D 是否写入记忆。超过相关视角阈值的帧记录为 `attack_exposure_rejected`，不计剂量。

### L3 stale conflict

1. 为同一个真实 Treasure 创建 `Stale Old Anchor` 和 `Stale New Anchor`。
2. 将该 Treasure 拖到 `Stale Treasure`。
3. 设置 `Condition=L3 Stale Conflict`、`Stale Exposure Count=5`，必须等于 `Controlled Prelude Observations`。
4. 达到旧位置曝光剂量后，控制器把同一稳定对象 ID 移至新 anchor，并记录 `stale_transition`。

L3 的旧记忆在写入时是真实的，移动后才变陈旧；分析时必须用 `stale_transition` 的时间判定动态有效性，不能把该 memory 永久标成普通 decoy poison。

## 5. 每轮实际运行

1. 在 PowerShell 启动服务：

   ```powershell
   Set-Location E:\study\gewu\unity
   $env:DEEPSEEK_API_KEY = "<仅放在环境变量中的密钥>"
   python .\vrag_experiemt.py
   ```

2. Unity 进 Play 后不要再手动输入任务；Controller 会自动完成 health、reset 和固定任务注入。
3. 观察 `ExperimentTrialController.State`：必须按顺序进入 `Validating → CheckingServices → ResettingMemory → Validated → TaskInjected → ControlledExposure → Running`；`Running` 前不得出现 `action_dispatched`。
4. 若预检进入 `Failed`，在 `preflight_failures` 中保留原因；若受控预观测失败，会生成诊断性 `summary.json`，但必须排除在论文统计之外。
5. 完成、超时或错误后退出 Play。每个 trial 重新加载场景，不在同一次 Play 中仅重置状态机。

输出目录：

```text
%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\vrag_experiments\<run_id>\
  events.jsonl
  summary.json
  replay\manifest.json
  replay\frames.jsonl
  replay\frames\*.jpg
  replay\vision\*.json
  replay\planner\*.json
```

每轮至少检查：

```text
run_started 早于 vlm_call / retrieval / decision / action_dispatched
frame_captured 的 JPEG SHA-256 可复算
object_claim 带 claim_id、bbox、alignment_status、matched_object_id
object_miss 覆盖可见但未检出的 Treasure
decision → action_dispatched → action_completed 使用同一 decision_id
configuration_valid=true
attack_dose_achieved=true（L1/L2/L3）
```

## 6. 固定回放与反事实 case

把多个 run 生成固定索引：

```powershell
python .\Experiments\build_counterfactual_replay.py `
  "<vrag_experiments 根目录>" `
  --output ".\Experiments\replay_corpus_v1"
```

输出：

```text
frame_index.jsonl            每张图的路径与 hash 校验
counterfactual_cases.jsonl   factual 与移除无效 evidence 后的上下文对
summary.json
```

该脚本只构建审计和 mask case，不调用规划器。下一步要用同一冻结模型、temperature=0、相同完整 messages 分别执行 factual 与 masked 输入；只有目的地改变才能称为“该次决策层面的直接因果证据”，不能外推为整条 episode 的因果效应。

## 7. 分阶段实验

### 阶段 0：协议验收，不进论文主表

```text
1 个 development 布局 × A/B/C/D × L0/L1 × 3 matched seeds = 24 轮
```

通过标准：0 个跨轮记忆；0 个启动前动作；对象匹配人工抽查一致率达预设阈值；所有图像 hash 完整；L1 剂量一致。

### 阶段 1：校准

在独立 calibration layouts 采集对象级肯定 claim。最低门槛仍为 100 条且正确/错误各至少 10 条；正式研究应由 pilot 方差和类别比例决定更大的样本量。只用 calibration split 拟合 D 的可靠性和 write/verify/pursue 阈值。

### 阶段 2：攻击有效性 pilot

```text
2 个 development 布局 × L1/L2/L3 × 10 seeds
```

先验证攻击确实改变 VLM 错误率、重复曝光剂量和 stale 检索率；若操纵检查失败，不进入方法比较。

### 阶段 3：在线闭环主实验

建议初始设计：

```text
至少 3 个 calibration 外布局 × A/B/C/D × L0/L1/L2/L3 × matched seeds
```

seed 数量用 pilot 的任务成功率/攻击成功率做功效分析后冻结。报告 mixed-effects 模型或按 layout 分层的置信区间，不能把同一布局内的帧当独立样本。

### 阶段 4：held-out 泛化

至少增加未参与开发和校准的布局、诱饵外观及第二个 VLM。若资源允许，再做少量真实机器人复现；单 Town、单黄色方块只能支撑系统原型结论。

## 8. 当前仍是正式主表前的硬门槛

1. 当前对象级 claim 已准确评分，但 Agent 仍可能把同一帧多个 claim 合成一个 memory entry；投稿主实验前应改为“一 claim 一 canonical memory_id”。
2. 增加 ID-mask Camera，并对几何 bounds GT 做人工抽样审计。
3. 执行同模型 factual/masked planner replay；现有脚本只生成 case。
4. 将 retrieval 日志升级为完整候选池、各分项分数和 selected IDs，而不是只保存格式化文本。
5. L2/L3 先做操纵检查，并冻结曝光阈值；不得根据 test 结果调参。
6. 完成多布局、第二 VLM、统计功效与 held-out test 后，才可评估 ICRA 论文强度。
