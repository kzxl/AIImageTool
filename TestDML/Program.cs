using System; using Microsoft.ML.OnnxRuntime; class Program { static void Main() { var opt = new SessionOptions(); try { opt.AppendExecutionProvider_DML(10); Console.WriteLine(\
OK
10\); } catch (Exception e) { Console.WriteLine(\Err
10:
\ + e.Message); } } }
