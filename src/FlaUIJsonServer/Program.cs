using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace FlaUIJsonServer
{
    /// <summary>
    /// 任务请求模型
    /// </summary>
    public class TaskRequest
    {
        /// <summary>
        /// 任务唯一标识
        /// </summary>
        public string TaskId { get; set; } = string.Empty;

        /// <summary>
        /// 测试用例名称（对应 tests 文件夹下的类名）
        /// </summary>
        public string TestCase { get; set; } = string.Empty;

        /// <summary>
        /// 结果回调 URL
        /// </summary>
        public string CallbackUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// 任务接收响应模型
    /// </summary>
    public class TaskAcceptedResponse
    {
        public string Status { get; set; } = "accepted";
        public string TaskId { get; set; } = string.Empty;
        public string Message { get; set; } = "任务已加入后台队列";
    }

    /// <summary>
    /// 测试结果模型（用于 Webhook 回调）
    /// </summary>
    public class TestResult
    {
        public string TaskId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // success 或 fail
        public string Logs { get; set; } = string.Empty;
    }

    public class Program
    {
        // 全局静态 HttpClient，避免套接字耗尽，提高性能
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30) // 设置合理的超时时间
        };

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 配置日志
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            var app = builder.Build();

            // 健康检查端点
            app.MapGet("/", () => "FlaUIJsonServer is running! POST to /api/run_task to submit tasks.");

            // ============================================================
            // 核心端点：接收测试任务
            // ============================================================
            app.MapPost("/api/run_task", async (TaskRequest request) =>
            {
                try
                {
                    // 记录接收到的任务
                    app.Logger.LogInformation($"[收到任务] TaskId: {request.TaskId}, TestCase: {request.TestCase}, Callback: {request.CallbackUrl}");

                    // 参数验证
                    if (string.IsNullOrWhiteSpace(request.TaskId))
                    {
                        app.Logger.LogWarning("[参数错误] TaskId 为空");
                        return Results.BadRequest(new { status = "error", message = "TaskId 不能为空" });
                    }

                    if (string.IsNullOrWhiteSpace(request.TestCase))
                    {
                        app.Logger.LogWarning("[参数错误] TestCase 为空");
                        return Results.BadRequest(new { status = "error", message = "TestCase 不能为空" });
                    }

                    if (string.IsNullOrWhiteSpace(request.CallbackUrl))
                    {
                        app.Logger.LogWarning("[参数错误] CallbackUrl 为空");
                        return Results.BadRequest(new { status = "error", message = "CallbackUrl 不能为空" });
                    }

                    // ============================================================
                    // 🔥 关键：立即返回响应，不等待测试执行完成
                    // ============================================================
                    var response = new TaskAcceptedResponse
                    {
                        TaskId = request.TaskId,
                        Status = "accepted",
                        Message = "任务已加入后台队列"
                    };

                    // ============================================================
                    // 🔥 核心：在后台 STA 线程中异步执行测试
                    // 注意：绝对不能使用 Task.Run，因为线程池默认是 MTA
                    // ============================================================
                    Thread testThread = new Thread(() => RunTestWrapper(request.TaskId, request.TestCase, request.CallbackUrl, app.Logger));
                    testThread.SetApartmentState(ApartmentState.STA); // 必须设置为 STA
                    testThread.IsBackground = true; // 设置为后台线程，避免阻塞应用退出
                    testThread.Start();

                    app.Logger.LogInformation($"[任务已调度] TaskId: {request.TaskId}, 线程已启动");

                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    app.Logger.LogError($"[任务接收失败] TaskId: {request.TaskId}, 错误: {ex.Message}");
                    return Results.StatusCode(500);
                }
            });

            app.Run();
        }

        /// <summary>
        /// STA 线程测试包装方法
        /// 在这个方法中执行所有的 FlaUI UI 自动化逻辑
        /// </summary>
        /// <param name="taskId">任务 ID</param>
        /// <param name="testCase">测试用例名称</param>
        /// <param name="callbackUrl">回调 URL</param>
        /// <param name="logger">日志记录器</param>
        static void RunTestWrapper(string taskId, string testCase, string callbackUrl, ILogger logger)
        {
            StringBuilder logs = new StringBuilder();
            string status = "fail";

            try
            {
                logs.AppendLine($"[开始执行] TaskId: {taskId}, TestCase: {testCase}");
                logs.AppendLine($"[时间] {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                logger.LogInformation($"[测试开始] TaskId: {taskId}, TestCase: {testCase}");

                // ============================================================
                // 根据测试用例名称执行对应的测试
                // 注意：这里需要根据实际的 tests 文件夹下的类进行反射调用
                // 为了演示，这里模拟执行测试
                // ============================================================

                switch (testCase.ToLower())
                {
                    case "login_test":
                        // 这里可以调用 tests.Login 类中的测试方法
                        // 实际项目中，你应该通过反射动态调用测试类的方法
                        logs.AppendLine("[执行测试] 登录测试");
                        logs.AppendLine("[步骤1] 启动模拟器生态");
                        logs.AppendLine("[步骤2] 启动 UI 应用程序");
                        logs.AppendLine("[步骤3] 执行登录操作");
                        
                        // 模拟测试执行
                        Thread.Sleep(2000); // 模拟测试耗时
                        
                        logs.AppendLine("[测试结果] 登录操作执行成功");
                        status = "success";
                        break;

                    case "other_test":
                        logs.AppendLine("[执行测试] 其他测试用例");
                        Thread.Sleep(1000);
                        logs.AppendLine("[测试结果] 测试完成");
                        status = "success";
                        break;

                    default:
                        logs.AppendLine($"[错误] 未知的测试用例: {testCase}");
                        status = "fail";
                        break;
                }

                logs.AppendLine($"[完成时间] {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                logger.LogInformation($"[测试完成] TaskId: {taskId}, Status: {status}");
            }
            catch (Exception ex)
            {
                status = "fail";
                logs.AppendLine($"[异常] 测试执行失败: {ex.Message}");
                logs.AppendLine($"[堆栈] {ex.StackTrace}");
                logger.LogError($"[测试异常] TaskId: {taskId}, 错误: {ex.Message}");
            }
            finally
            {
                // ============================================================
                // 🔥 核心：执行完毕后发送 Webhook 回调
                // ============================================================
                SendWebhookCallback(taskId, status, logs.ToString(), callbackUrl, logger);
            }
        }

        /// <summary>
        /// 发送 Webhook 回调
        /// </summary>
        /// <param name="taskId">任务 ID</param>
        /// <param name="status">测试状态（success 或 fail）</param>
        /// <param name="logs">执行日志</param>
        /// <param name="callbackUrl">回调 URL</param>
        /// <param name="logger">日志记录器</param>
        static async void SendWebhookCallback(string taskId, string status, string logs, string callbackUrl, ILogger logger)
        {
            try
            {
                // 构造结果 JSON
                var result = new TestResult
                {
                    TaskId = taskId,
                    Status = status,
                    Logs = logs
                };

                string jsonPayload = JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    WriteIndented = true // 格式化 JSON，方便调试
                });

                logger.LogInformation($"[发送回调] 目标: {callbackUrl}, TaskId: {taskId}, Status: {status}");
                logger.LogInformation($"[回调内容] {jsonPayload}");

                // 使用静态 HttpClient 发送 POST 请求
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                
                HttpResponseMessage response = await _httpClient.PostAsync(callbackUrl, content);
                
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation($"[回调成功] TaskId: {taskId}, 状态码: {response.StatusCode}");
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    logger.LogWarning($"[回调失败] TaskId: {taskId}, 状态码: {response.StatusCode}, 响应: {errorContent}");
                }
            }
            catch (TaskCanceledException ex)
            {
                // 请求超时
                logger.LogError($"[回调超时] TaskId: {taskId}, 错误: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                // 网络错误（连接失败、DNS 解析失败等）
                logger.LogError($"[网络错误] TaskId: {taskId}, 错误: {ex.Message}");
            }
            catch (Exception ex)
            {
                // 其他异常
                logger.LogError($"[回调异常] TaskId: {taskId}, 错误: {ex.Message}");
                // 注意：这里不抛出异常，确保 Server 不会因为回调失败而崩溃
            }
        }
    }
}