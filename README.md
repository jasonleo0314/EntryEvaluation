# EntryEvaluation

EntryEvaluation 是一个基于 .NET 10 的交互式参赛作品评审与计分工具。程序通过 CSV 文件维护评分目录、参赛作品清单和默认权重，在控制台中引导评委逐项录入分值与备注，并自动生成评审结果、审计日志和标准化后的最终计分表。

## 功能概览

- 交互式控制台评审流程，支持中文界面与表格式进度展示。
- 通过 `Criteria.csv` 配置评分大项、子项、阶段、标题和说明。
- 通过 `Entries.csv` 配置待评审参赛作品清单。
- 通过 `Weights.csv` 配置各子项默认权重，并在运行时支持确认或调整。
- 每个子项支持录入 0~3 分和可选备注。
- 每个大项完成后支持录入大项备注。
- 每个参赛作品完成后支持录入项目总备注。
- 自动计算各大项得分和未标准化总分。
- 全部参赛作品完成后自动生成标准化计分表。
- 自动保存评审进度，异常退出或中断后可恢复未完成评审。
- 为每次评审生成 JSONL 审计日志，便于追溯分值、备注、权重和结果。
- 输出带 UTF-8 BOM 的 CSV，便于使用 Excel 打开中文内容。

## 项目结构

```text
EntryEvaluation/
├─ Program.cs                         # 控制台入口与主流程
├─ appsettings.json                   # 应用配置
├─ Criteria.csv                       # 评分目录示例
├─ Entries.csv                        # 参赛作品清单示例
├─ Weights.csv                        # 子项默认权重示例
├─ Models/                            # 领域模型
├─ Services/                          # CSV、评分、标准化、进度、审计和交互流程
├─ EntryEvaluation.csproj             # 主程序项目
└─ EntryEvaluation.Tests/             # xUnit 测试项目
```

## 环境要求

- .NET 10 SDK
- 支持 UTF-8 的终端环境
- Windows Terminal、PowerShell、Visual Studio 终端或其他现代控制台均可使用

项目主要依赖：

- `Spectre.Console`：用于控制台表格、面板、颜色和交互式显示。
- `xunit`：用于测试项目。

## 快速开始

### 1. 克隆或进入项目目录

```powershell
cd F:\Git\ForPolice\EntryEvaluation\EntryEvaluation
```

### 2. 还原依赖

```powershell
dotnet restore
```

### 3. 编译项目

```powershell
dotnet build
```

### 4. 运行程序

```powershell
dotnet run --project EntryEvaluation.csproj
```

程序启动后会显示当前使用的配置文件、评分目录、参赛作品清单、默认权重文件、输出文件和进度文件位置。随后按提示确认或调整权重，并选择参赛作品序号开始评审。

## 命令行参数

程序默认从当前目录或可执行文件目录读取 `appsettings.json`、`Criteria.csv`、`Entries.csv` 和 `Weights.csv`。也可以通过命令行参数指定自定义路径。

| 参数 | 说明 |
| --- | --- |
| `--config <path>` | 指定配置文件路径。 |
| `--criteria <path>` | 指定评分目录 CSV。 |
| `--Entries <path>` | 指定参赛作品清单 CSV。注意参数名当前为大写 `E`。 |
| `--weights <path>` | 指定默认权重 CSV。 |
| `--out <path>` | 指定未标准化结果 CSV 输出路径。 |
| `--standardized-out <path>` | 指定标准化结果 CSV 输出路径。 |
| `--progress <path>` | 指定进度文件路径。 |

示例：

```powershell
dotnet run --project EntryEvaluation.csproj -- \
  --config .\appsettings.json \
  --criteria .\Criteria.csv \
  --Entries .\Entries.csv \
  --weights .\Weights.csv \
  --out .\ReviewResults.csv \
  --standardized-out .\ReviewResults_Standardized.csv \
  --progress .\ReviewProgress.json
```

## 输入文件格式

### Criteria.csv：评分目录

`Criteria.csv` 定义评分大项和子项，表头必须为：

```csv
CategoryId,CategoryName,CategoryMaxPoints,SubId,Phase,SubTitle,SubDescription
```

字段说明：

| 字段 | 说明 |
| --- | --- |
| `CategoryId` | 大项编号。同一大项的编号必须一致。 |
| `CategoryName` | 大项名称。 |
| `CategoryMaxPoints` | 大项满分。所有大项满分合计应为 100。 |
| `SubId` | 子项编号，必须唯一。 |
| `Phase` | 子项所属评审阶段。 |
| `SubTitle` | 子项标题。 |
| `SubDescription` | 子项说明，评审时会显示给评委参考。 |

当前支持的阶段来自程序中的 `WorkflowPhase` 枚举，示例文件中包含：

- `ReadDocs`：阅读材料
- `EnterUI`：进入界面
- `ImportData`：导入数据
- `RunModel`：运行模型
- `GetResults`：获取结果
- `UnderstandResults`：理解结果

示例：

```csv
CategoryId,CategoryName,CategoryMaxPoints,SubId,Phase,SubTitle,SubDescription
C1,类别01,10,S1.1,ReadDocs,子项01,一般说明
C1,类别01,10,S1.2,ReadDocs,子项02,一般说明
```

### Entries.csv：参赛作品清单

`Entries.csv` 定义需要评审的参赛作品，表头必须为：

```csv
EntryName
```

示例：

```csv
EntryName
参赛作品01
参赛作品02
参赛作品03
```

### Weights.csv：子项默认权重

`Weights.csv` 定义每个子项的默认权重，表头必须为：

```csv
SubId,Weight
```

字段说明：

| 字段 | 说明 |
| --- | --- |
| `SubId` | 对应 `Criteria.csv` 中的子项编号。 |
| `Weight` | 子项权重，不能为负数。 |

权重会按大项归一化后用于计算，因此同一大项内权重通常建议合计为 1。即使默认权重合计不为 1，程序也会在权重确认流程中进行归一化。

示例：

```csv
SubId,Weight
S1.1,0.30
S1.2,0.35
S1.3,0.35
```

## 配置文件说明

默认配置文件为 `appsettings.json`。

```json
{
  "Paths": {
    "CriteriaFileName": "Criteria.csv",
    "EntriesFileName": "Entries.csv",
    "WeightsFileName": "Weights.csv",
    "ProgressFileName": "ReviewProgress.json",
    "AuditDirectoryName": "Audit",
    "RawResultsFileNamePattern": "ReviewResults_{timestamp}.csv",
    "StandardizedResultsSuffix": "_Standardized",
    "OutputTimestampFormat": "yyyyMMdd_HHmmss"
  },
  "Scoring": {
    "MinimumScore": 0,
    "MaximumScore": 3,
    "RequiredMinimum": 0.0,
    "RequiredMaximum": 100.0,
    "CategoryTotalPoints": 100.0,
    "RoundingDigits": 1,
    "Tolerance": 1e-6
  },
  "Standardization": {
    "TargetMean": 90.0,
    "TargetThreeStandardDeviations": 10.0,
    "Epsilon": 1e-9
  },
  "Display": {
    "SeparatorWidth": 70,
    "AppTitle": "参赛作品评审 — 交互式打分系统",
    "EntryNounSingular": "参赛作品",
    "EntryNameColumnHeader": "参赛作品名称",
    "ResultsCommentColumnHeader": "备注汇总",
    "ResultsTotalColumnHeader": "总分",
    "ShowWeightSummary": true
  }
}
```

### Paths

| 配置项 | 说明 |
| --- | --- |
| `CriteriaFileName` | 默认评分目录文件名。 |
| `EntriesFileName` | 默认参赛作品清单文件名。 |
| `WeightsFileName` | 默认权重文件名。 |
| `ProgressFileName` | 默认进度文件名。 |
| `AuditDirectoryName` | 审计日志目录名。 |
| `RawResultsFileNamePattern` | 未标准化结果文件名模板，支持 `{timestamp}`。 |
| `StandardizedResultsSuffix` | 标准化结果文件名后缀。 |
| `OutputTimestampFormat` | 输出文件时间戳格式。 |

### Scoring

| 配置项 | 说明 |
| --- | --- |
| `MinimumScore` | 子项最小分。 |
| `MaximumScore` | 子项最大分。默认 3。 |
| `RequiredMinimum` | 总分允许的最小值。 |
| `RequiredMaximum` | 总分允许的最大值。 |
| `CategoryTotalPoints` | 总分满分。默认 100。 |
| `RoundingDigits` | 分数保留小数位数。 |
| `Tolerance` | 浮点计算容差。 |

### Standardization

| 配置项 | 说明 |
| --- | --- |
| `TargetMean` | 标准化目标均值。默认 90。 |
| `TargetThreeStandardDeviations` | 三倍标准差对应的目标区间宽度。默认 10。 |
| `Epsilon` | 判断标准差接近 0 的阈值。 |

### Display

| 配置项 | 说明 |
| --- | --- |
| `SeparatorWidth` | 分隔线宽度。 |
| `AppTitle` | 控制台标题。 |
| `EntryNounSingular` | 参赛对象的显示名称。 |
| `EntryNameColumnHeader` | 结果 CSV 中参赛作品列名。 |
| `ResultsCommentColumnHeader` | 结果 CSV 中备注汇总列名。 |
| `ResultsTotalColumnHeader` | 结果 CSV 中总分列名。 |
| `ShowWeightSummary` | 是否在评审开始时显示权重汇总。 |

## 评审流程

1. 程序启动并加载配置文件。
2. 加载评分目录、参赛作品清单和默认权重。
3. 显示文件路径、输出路径和进度文件位置。
4. 评委确认或调整各子项权重。
5. 程序显示参赛作品列表。
6. 评委输入作品序号开始评审。
7. 程序按阶段和子项编号逐项提示录入分值。
8. 每个子项可录入备注，也可直接回车跳过。
9. 每个大项全部子项完成后录入大项备注。
10. 每个作品全部子项完成后录入项目总备注。
11. 程序计算并显示该作品未标准化得分。
12. 全部作品完成后写出未标准化结果 CSV 和标准化结果 CSV。

评审列表界面支持：

| 输入 | 作用 |
| --- | --- |
| 数字序号 | 开始评审对应参赛作品。 |
| `s` | 查看当前汇总。 |
| `q` | 退出程序并保存已完成结果。 |

## 计分规则

每个子项录入整数分，默认范围为 0~3。

程序计算逻辑：

1. 子项得分归一化：`子项得分 / MaximumScore`。
2. 同一大项内使用归一化后的子项权重计算加权率。
3. 大项得分 = `大项满分 × 大项加权率`。
4. 总分 = 所有大项得分之和。
5. 分数按 `RoundingDigits` 配置进行四舍五入。

示例：某大项满分 10，包含三个子项，最终权重分别为 0.30、0.35、0.35，分值分别为 3、2、1，则：

```text
大项加权率 = (3 / 3) × 0.30 + (2 / 3) × 0.35 + (1 / 3) × 0.35
大项得分 = 10 × 大项加权率
```

## 标准化规则

全部参赛作品完成评审后，程序会对所有作品的总分进行标准化处理：

1. 计算所有作品未标准化总分的均值和标准差。
2. 使用 z-score 将总分映射到目标均值附近。
3. 默认目标均值为 90。
4. 默认三倍标准差对应 10 分区间。
5. 标准化后的总分会限制在 `RequiredMinimum` 到 `RequiredMaximum` 范围内。
6. 标准化总分再按原始大项得分比例分配回各大项。
7. 任一大项不会超过其配置的满分。

如果所有作品原始总分完全一致或标准差接近 0，则所有作品的标准化总分会使用 `TargetMean`。

## 输出文件

### 未标准化结果 CSV

默认文件名类似：

```text
ReviewResults_20250101_120000.csv
```

列结构：

```text
参赛作品名称,各大项得分...,总分,备注汇总
```

备注汇总会合并：

- 项目总备注
- 大项备注
- 子项备注

### 标准化结果 CSV

默认文件名类似：

```text
ReviewResults_20250101_120000_Standardized.csv
```

该文件仅在全部参赛作品完成评审后生成，结构与未标准化结果 CSV 相同，但大项得分和总分为标准化后的结果。

### 进度文件

默认文件名：

```text
ReviewProgress.json
```

程序会在评审过程中持续保存进度。若程序异常退出、中断或关闭，下次启动时会检测未完成评审，并询问是否恢复。

### 审计日志

默认目录：

```text
Audit/
```

每个参赛作品评审会生成一个 JSONL 文件，内容包含：

- 评审开始时间
- 权重快照
- 每个子项的分值和备注
- 大项备注
- 项目总备注
- 计算结果

审计日志适合用于赛后复核和问题追踪。

## 常见操作

### 使用自定义参赛作品清单

```powershell
dotnet run --project EntryEvaluation.csproj -- --Entries .\MyEntries.csv
```

### 输出到指定目录

```powershell
dotnet run --project EntryEvaluation.csproj -- --out .\output\ReviewResults.csv --standardized-out .\output\ReviewResults_Standardized.csv
```

### 使用独立进度文件

```powershell
dotnet run --project EntryEvaluation.csproj -- --progress .\progress\ReviewProgress_Round1.json
```

### 中途退出后继续

1. 正常输入 `q` 或按 `Ctrl+C` 退出。
2. 程序会尽量写出已完成作品的汇总结果。
3. 下次运行时使用同一个进度文件。
4. 按提示选择是否恢复未完成评审。

## 测试

运行全部测试：

```powershell
dotnet test
```

测试项目位于 `EntryEvaluation.Tests/`，覆盖 CSV 解析、权重处理、评分计算、标准化、结果写出、审计日志和评审流程等核心逻辑。

## 开发说明

- 目标框架：`net10.0`
- 启用隐式 using：`ImplicitUsings`
- 启用可空引用类型：`Nullable`
- 主项目类型：控制台应用
- 测试框架：xUnit

建议开发流程：

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project EntryEvaluation.csproj
```

## 数据准备建议

- 使用 Excel 编辑 CSV 时，建议保存为 UTF-8 CSV，避免中文乱码。
- `Criteria.csv` 中同一 `CategoryId` 的 `CategoryName` 和 `CategoryMaxPoints` 必须保持一致。
- `SubId` 不能重复。
- `Weights.csv` 中的 `SubId` 应覆盖所有评分子项。
- 权重不能为负数。
- 参赛作品名称不应为空。
- 修改评分目录后，建议重新检查权重文件是否仍与子项匹配。

## 故障排查

### CSV 表头不匹配

请检查 CSV 第一行是否与程序要求完全一致。字段名大小写不敏感，但字段顺序需要匹配。

### 中文显示乱码

请确认：

- 终端支持 UTF-8。
- CSV 使用 UTF-8 编码保存。
- 输出 CSV 使用支持 UTF-8 BOM 的软件打开。

### 权重校验失败

请检查：

- `Weights.csv` 是否包含未知 `SubId`。
- 是否遗漏了 `Criteria.csv` 中的某些子项。
- 权重是否为负数。
- 同一大项内最终权重归一化后是否可正常计算。

### 无法恢复进度

请确认：

- 使用的是同一个 `ReviewProgress.json`。
- `Entries.csv` 中仍包含未完成评审的参赛作品名称。
- 评分目录和权重文件没有发生不兼容变更。

### 结果文件写出失败

请检查：

- 输出目录是否存在或是否有权限创建。
- 目标 CSV 是否正在被 Excel 或其他程序占用。
- `--out` 或 `--standardized-out` 路径是否合法。

## 退出码

| 退出码 | 含义 |
| --- | --- |
| `0` | 正常完成或正常退出。 |
| `1` | 配置或输入 CSV 加载失败。 |
| `2` | 结果文件写出失败。 |
| `3` | 程序运行过程中发生可处理异常。 |
| `130` | 收到中断请求后退出。 |

## 适用场景

本工具适合用于需要人工逐项评分、保留备注、支持复核追踪，并最终输出汇总 CSV 的评审场景，例如：

- 比赛作品评审
- 原型系统测评
- 工具能力打分
- 项目方案评估
- 多候选对象的标准化评分

## 许可证

当前仓库未声明许可证。如需发布或分发，请先补充明确的许可证文件。
