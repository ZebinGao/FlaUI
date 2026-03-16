using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using System.Diagnostics;

namespace FlaUIJsonServer.tests
{
	public class Login
	{
		// 🔥 极其重要：UI自动化必须在这个单线程单元里跑
		[STAThread]
		static void Test_login()
		{
			Console.WriteLine("======================================================");
			Console.WriteLine("开始执行本地直连测试: 验证登录成功 (脱离 WinAppDriver)");
			Console.WriteLine("======================================================\n");

			// 1. 配置路径 (请替换为你电脑上真实的路径，模拟你 Python 中的 config.json)
			string simulatorPath = @"D:\RDC-Soft\RDC-Soft\trunk\Venus\Venus2200\005\Shared\Simulator\Venus2200Simulator.exe";
			string rtPath = @"D:\RDC-Soft\RDC-Soft\trunk\Venus\Venus2200\005\Shared\Venus2200RT.exe";
			string dataCollectionPath = @"D:\RDC-Soft\RDC-Soft\trunk\Venus\Venus2200\Shared\VenusDataCollection.exe";
			string uiPath = @"D:\RDC-Soft\RDC-Soft\trunk\Venus\Venus2200\Shared\VenusUI.exe"; // 替换成实际的 UI 程序名

			Application simuApp = null;
			Application uiApp = null;

			try
			{
				// =========================================================
				// [测试步骤1] 启动 Simu 会话生态
				// =========================================================
				Console.WriteLine("\n[测试步骤1] 启动Simu生态...");

				// 1. 用最安全的原生方式启动它，确保窗口能弹出来
				var simuStartInfo = new ProcessStartInfo(simulatorPath)
				{
					WorkingDirectory = Path.GetDirectoryName(simulatorPath),
					UseShellExecute = true
				};
				Process rawSimuProcess = Process.Start(simuStartInfo);
				Console.WriteLine($"[Simu会话] 模拟器进程已拉起，PID: {rawSimuProcess.Id}");

				// 2. 极其重要：给它一点时间把 GUI 界面画出来
				// 如果你的模拟器启动很慢，这里可以改成 5000 甚至 8000
				Console.WriteLine("等待模拟器界面初始化...");
				Thread.Sleep(4000);

				// 3. FlaUI 强行接管！
				// 通过进程 ID 把原生的 Process 对象转换成 FlaUI 可以控制的 Application 对象
				simuApp = Application.Attach(rawSimuProcess.Id);
				Console.WriteLine($"[Simu会话] FlaUI 已成功接管模拟器！");

				// 之后你就可以像控制 uiApp 一样，去查找 simuApp 里的元素了
				// var simuWindow = simuApp.GetMainWindow(automation);

				// 启动 RT 程序 (使用原生的 Process.Start，替代 subprocess.Popen)
				Process.Start(new ProcessStartInfo(rtPath) { WorkingDirectory = Path.GetDirectoryName(rtPath), UseShellExecute = true });// 🔥 核心魔法：相当于 Python 的 CREATE_NEW_CONSOLE 
				Console.WriteLine($"[Simu会话] 已启动: RT程序");
				Thread.Sleep(3000);

				// 启动数据采集程序
				Process.Start(new ProcessStartInfo(dataCollectionPath) { WorkingDirectory = Path.GetDirectoryName(dataCollectionPath), UseShellExecute = true });
				Console.WriteLine($"[Simu会话] 已启动: 数据采集程序");
				Thread.Sleep(3000);

				// =========================================================
				// [测试步骤2] 启动 UI 应用程序
				// =========================================================
				Console.WriteLine("\n[测试步骤2] 启动UI应用程序...");
				uiApp = Application.Launch(uiPath);

				// 初始化 UIA3 自动化对象 (你的终极驱动)
				using var automation = new UIA3Automation();

				// 🔥 FlaUI 魔法：不需要像 Python 那样死等 6 秒然后切窗口句柄！
				// GetMainWindow 会智能等待并直接抓取主窗口，默认超时 10 秒
				Console.WriteLine("等待主窗口加载...");
				var mainWindow = uiApp.GetMainWindow(automation, TimeSpan.FromSeconds(25));
				if (mainWindow == null) throw new Exception("获取登录窗口超时！");

				Console.WriteLine($"[UI会话] 成功抓取窗口: {mainWindow.Title}");

				// =========================================================
				// [测试步骤3] 执行登录操作
				// =========================================================
				Console.WriteLine("\n[测试步骤3] 执行登录操作...");

				// 相当于 Python 的 find_element_by_accessibility_id("pwd")
				var pwdInput = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("pwd"))?.AsTextBox();
				if (pwdInput == null) throw new Exception("找不到 Accessibility ID 为 'pwd' 的元素");

				// 相当于 Python 的 clear() 和 send_keys()
				pwdInput.Text = "";
				pwdInput.Enter("sysadmin");
				Console.WriteLine("[SmartDriver] 已输入文本到 [pwd]: sysadmin");

				// 相当于 Python 的 click("btnLogin")
				var btnLogin = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("btnLogin"))?.AsButton();
				if (btnLogin == null) throw new Exception("找不到 Accessibility ID 为 'btnLogin' 的元素");

				// Invoke() 是通过 UI 模式触发点击，比物理模拟鼠标 Click() 更稳、更快
				btnLogin.Invoke();
				Console.WriteLine("[SmartDriver] 已点击元素 [btnLogin]");

				Console.WriteLine("\n[测试结果] 登录操作执行成功");
				Console.WriteLine("======================================================\n");

				// 暂停一下看结果
				Thread.Sleep(3000);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"\n[测试失败] 发生异常: {ex.Message}");
				Console.WriteLine(ex.StackTrace);
			}
			finally
			{
				// =========================================================
				// [清理] 杀掉所有进程
				// =========================================================
				Console.WriteLine("\n[清理] 正在清理进程...");

				// 优雅关闭
				uiApp?.Close();
				simuApp?.Close();
				Thread.Sleep(1000);

				// 对应你的 terminate_processes_by_names，C# 自带强大的进程管理
				string[] processNamesToKill = {
					Path.GetFileNameWithoutExtension(simulatorPath),
					Path.GetFileNameWithoutExtension(rtPath),
					Path.GetFileNameWithoutExtension(dataCollectionPath),
					Path.GetFileNameWithoutExtension(uiPath)
				};

				foreach (var pName in processNamesToKill)
				{
					foreach (var process in Process.GetProcessesByName(pName))
					{
						try
						{
							process.Kill();
							Console.WriteLine($"[已终止] 进程: {pName}.exe");
						}
						catch { /* 忽略权限错误或已退出的进程 */ }
					}
				}
				Console.WriteLine(new string('=', 70) + "\n");
			}
		}
	}
}
