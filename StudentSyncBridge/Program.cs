using System;
using System.Configuration;
using System.Globalization;
using System.Threading;

namespace StudentSyncBridge
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string middleware = ConfigurationManager.AppSettings["MiddlewareUrl"] ?? "http://127.0.0.1:50931";
            string payment = ConfigurationManager.AppSettings["PaymentBridge"] ?? "127.0.0.1:7793";
            int port = 7792;
            int configuredPort;
            if (int.TryParse(ConfigurationManager.AppSettings["ListenPort"], NumberStyles.Integer, CultureInfo.InvariantCulture, out configuredPort))
            {
                port = configuredPort;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--middleware" && i + 1 < args.Length)
                {
                    middleware = args[++i];
                }
                else if (args[i] == "--port" && i + 1 < args.Length)
                {
                    int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out port);
                }
                else if (args[i] == "--payment" && i + 1 < args.Length)
                {
                    payment = args[++i];
                }
            }

            middleware = middleware.Trim().TrimEnd('/');
            var server = new BridgeServer(middleware, port, payment);
            server.Start();
            Console.WriteLine("StudentSyncBridge listening on port " + port.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("Middleware " + middleware);
            if (server.PassesPaymentToGateBridge)
            {
                Console.WriteLine("Payment bridge " + server.PaymentAddress);
            }
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }
    }
}
