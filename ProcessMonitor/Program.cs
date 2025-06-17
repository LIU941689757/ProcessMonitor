using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

class Program
{
    static void Main()
    {
        string inputFile = @"D:\新建 文本文档.txt";  // 存放进程名的文本文件（每行一个进程名）
        string outputFile = @"D:\ProcessMonitorResults.txt"; // 监控结果输出文件

        if (!File.Exists(inputFile))
        {
            Console.WriteLine($"找不到进程列表文件：{inputFile}");
            return;
        }

        // 读取所有要监控的进程名，去除空行和空白字符
        string[] processesToMonitor = File.ReadAllLines(inputFile);
        processesToMonitor = Array.FindAll(processesToMonitor, s => !string.IsNullOrWhiteSpace(s));

        // 为每个进程名启动独立监控线程
        foreach (var pnameRaw in processesToMonitor)
        {
            string pname = pnameRaw.Trim();

            new Thread(() =>
            {
                int count = 0; // 每个进程独立计数器

                while (true)
                {
                    Console.WriteLine($"等待进程启动: {pname}");

                    Process p = null;
                    // 等待进程启动
                    while (p == null)
                    {
                        var arr = Process.GetProcessesByName(pname);
                        if (arr.Length > 0)
                        {
                            p = arr[0];
                            count++;
                            Console.WriteLine($"检测到进程 #{count} {pname} PID={p.Id} 启动时间 {p.StartTime}");
                            break;
                        }
                        Thread.Sleep(1000);
                    }

                    // 获取性能计数器实例名
                    string inst = null;
                    var cat = new PerformanceCounterCategory("Process");
                    foreach (var i in cat.GetInstanceNames())
                    {
                        if (i.StartsWith(pname))
                        {
                            inst = i;
                            break;
                        }
                    }
                    if (inst == null)
                    {
                        Console.WriteLine("找不到性能计数器实例，跳过该进程监控");
                        continue;
                    }

                    var cpu = new PerformanceCounter("Process", "% Processor Time", inst);
                    var ioR = new PerformanceCounter("Process", "IO Read Bytes/sec", inst);
                    var ioW = new PerformanceCounter("Process", "IO Write Bytes/sec", inst);

                    cpu.NextValue();
                    ioR.NextValue();
                    ioW.NextValue();

                    long ioReadSum = 0;
                    long ioWriteSum = 0;
                    float memPeak = 0;
                    DateTime start = p.StartTime;

                    // 监控进程直到退出
                    while (!p.HasExited)
                    {
                        try
                        {
                            float mem = p.WorkingSet64 / 1024f / 1024f;
                            if (mem > memPeak) memPeak = mem;

                            ioReadSum += (long)ioR.NextValue();
                            ioWriteSum += (long)ioW.NextValue();

                            Thread.Sleep(1000);
                        }
                        catch
                        {
                            break;
                        }
                    }

                    DateTime end;
                    try { end = p.ExitTime; }
                    catch { end = DateTime.Now; }

                    TimeSpan dur = end - start;
                    TimeSpan cputime = TimeSpan.Zero;
                    try { cputime = p.TotalProcessorTime; } catch { }

                    double cpuPercent = 0;
                    if (dur.TotalSeconds > 0)
                        cpuPercent = (cputime.TotalMilliseconds / (Environment.ProcessorCount * dur.TotalMilliseconds)) * 100;

                    string result = $"进程执行 #{count} ({pname}) 结果：\r\n" +
                        $"启动时间: {start}\r\n" +
                        $"退出时间: {end}\r\n" +
                        $"运行时长: {dur.TotalSeconds:F1}秒\r\n" +
                        $"平均CPU使用率: {cpuPercent:F1}%\r\n" +
                        $"内存峰值: {memPeak:F1}MB\r\n" +
                        $"磁盘读总量: {ioReadSum / 1024f:F1}KB\r\n" +
                        $"磁盘写总量: {ioWriteSum / 1024f:F1}KB\r\n" +
                        new string('-', 40) + "\r\n";

                    Console.WriteLine(result);

                    try
                    {
                        File.AppendAllText(outputFile, result);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"写入文件失败: {ex.Message}");
                    }

                    // 等待进程重新启动
                }

            }).Start(); // 启动线程
        }

        Console.WriteLine("监控已启动，按 Ctrl+C 退出程序...");
        Thread.Sleep(Timeout.Infinite); // 主线程阻塞，保持后台线程运行
    }
}
