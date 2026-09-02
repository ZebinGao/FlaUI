# FlaUIJsonServer

基于 [FlaUI](https://github.com/FlaUI/FlaUI) 的 Windows UI 自动化执行服务。

部署在被测机器上，通过 HTTP 接收外部测试平台下发的测试任务，在后台 STA 线程中用 FlaUI 操控被测程序的界面，执行完毕后通过 Webhook 把结果回调给平台。

## 架构与任务执行流程

```
测试平台 ──POST /api/run_task──► FlaUIJsonServer（立即返回 accepted）
                                        │
                                        ▼  new Thread + SetApartmentState(STA)
                                  RunTestWrapper 后台执行
                                  （拉起被测程序、FlaUI 元素查找与操作）
                                        │
                                        ▼  finally 中回调
                                  POST 结果到 callback_url ──► 测试平台
```

三段异步设计：接口**立即**返回任务已受理，不阻塞平台的请求；测试在线程池之外的专用 STA 线程上执行；结果通过回调异步送达。因此平台侧需要通过 `task_id` 串联一次任务的请求与结果。

## 环境要求

- Windows + .NET 8 SDK（项目为 `net8.0-windows`）
- 被测程序套件已安装（当前用例针对 Venus2200：模拟器 / RT / 数据采集 / UI 四个程序）
- 本项目以**源码级 ProjectReference** 方式引用 `FlaUI.Core` 和 `FlaUI.UIA3`（见 `FlaUIJsonServer.csproj`），首次构建会连带编译整个解决方案中的这两个库

## 启动

**IDE 调试（推荐）**：选中本项目按 F5，走 `Properties/launchSettings.json` 的 `http` profile，监听 `http://localhost:5123`（`launchBrowser` 已设为 `false`，调试时不会弹浏览器）。

**命令行**：

```bash
cd src/FlaUIJsonServer
dotnet run
```

## API

### GET /

健康检查，返回一行服务状态文字。

### POST /api/run_task

提交测试任务。请求体字段为 snake_case（通过 `JsonPropertyName` 映射到 C# 属性）：

```json
{
  "task_id": "T-20260902-001",
  "test_case_id": "login",
  "test_case_name": "登录测试",
  "paths": {
    "simulator": "D:\\RDC-Soft\\...\\Venus2200Simulator.exe",
    "rt": "D:\\RDC-Soft\\...\\Venus2200RT.exe",
    "dataCollection": "D:\\RDC-Soft\\...\\VenusDataCollection.exe",
    "ui": "D:\\RDC-Soft\\...\\VenusUI.exe"
  },
  "callback_url": "https://webhook.site/xxxx",
  "timestamp": "2026-09-02 10:00:00"
}
```

| 字段 | 必填 | 说明 |
|---|---|---|
| `task_id` | 是 | 任务唯一标识，用于关联回调结果 |
| `test_case_id` | 是 | 测试用例 ID，对应 `tests/` 文件夹下的用例类（如 `login`） |
| `test_case_name` | 否 | 测试用例名称，仅用于日志展示 |
| `paths` | 是 | 程序路径配置字典；其中 `simulator` 与 `ui` 为必填项，`rt`、`dataCollection` 等视用例而定 |
| `callback_url` | 是 | 结果回调地址 |
| `timestamp` | 否 | 任务下发时间戳 |

**响应**：

- `200`：`{"status": "accepted", "taskId": "...", "message": "任务已加入后台队列"}`（注意：只代表任务已受理，不代表测试成功）
- `400`：参数校验失败（`task_id` / `test_case_id` / `callback_url` / `paths.simulator` / `paths.ui` 为空）
- `500`：接收过程发生异常

### 回调格式

测试执行完毕后，服务向 `callback_url` 发送 POST（`Content-Type: application/json`），字段为 PascalCase：

```json
{
  "TaskId": "T-20260902-001",
  "Status": "success",
  "Logs": "[开始执行] ...\n[测试结果] 登录操作执行成功\n..."
}
```

`Status` 取值 `success` / `fail`；`Logs` 为执行过程的完整文本日志。回调失败（超时、网络错误）只记录服务端日志，不会重试。

## 目录结构

```
FlaUIJsonServer/
├── Program.cs                    # 服务入口：端点定义、任务调度、回调发送
├── tests/
│   └── Login.cs                  # 登录用例：拉起被测四程序 + FlaUI 登录操作
├── Properties/launchSettings.json
├── appsettings.json
└── FlaUIJsonServer.csproj
```

## 调试

- **断点**：`Program.cs` 中三处关键位置 —— 端点入口（`MapPost`）、`RunTestWrapper`（测试执行）、`SendWebhookCallback`（结果回调）。后台 STA 线程上的断点在 IDE 中照常命中。
- **日志**：控制台输出全链路日志：`[收到任务]` → `[任务已调度]` → `[测试开始]` → `[测试完成]` → `[发送回调]` / `[回调内容]`，不开调试器也能定位执行到哪一步。
- **回调接收**：本地调试 `callback_url` 可指向 [webhook.site](https://webhook.site) 等临时接收器，直接查看回发的 JSON；或直接看日志中 `[回调内容]` 一行。

## 关键实现细节

- **必须用专用 STA 线程而非 `Task.Run`**：FlaUI 底层的 UI Automation COM 组件要求单元线程模型为 STA，而线程池线程默认是 MTA。见 `Program.cs` 中 `testThread.SetApartmentState(ApartmentState.STA)`。
- **静态 `HttpClient`**：全局单例复用连接，避免套接字耗尽；超时 30 秒。
- **回调失败不抛异常**：`SendWebhookCallback` 捕获所有异常并仅记日志，保证服务本体不因回调目标不可达而崩溃。

## 当前状态与 TODO

- [x] 任务接收 / 参数校验 / 后台 STA 调度 / Webhook 回调链路
- [x] `tests/Login.cs` 登录用例本地直连跑通（路径硬编码，作为验证样例）
- [ ] `RunTestWrapper` 的 switch 分支接入 `tests/` 下的真实用例（目前为 `Thread.Sleep` 模拟执行；计划按 `test_case_id` 反射调用用例类，并把 `paths` 传入替代 Login.cs 中的硬编码路径）
- [ ] 回调失败重试 / 任务状态持久化
