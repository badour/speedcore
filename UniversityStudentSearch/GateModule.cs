using System;
using System.Web;

namespace UniversityStudentSearch
{
    public sealed class GateModule : IHttpModule
    {
        public void Init(HttpApplication application)
        {
            application.PostMapRequestHandler += OnPostMapRequestHandler;
        }

        public void Dispose()
        {
        }

        private static void OnPostMapRequestHandler(object sender, EventArgs e)
        {
            var application = (HttpApplication)sender;
            string path = (application.Request.Path ?? string.Empty).TrimEnd('/');
            if (path.EndsWith("/gate", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/gate/access", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/gate/commands", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/gate/ack", StringComparison.OrdinalIgnoreCase))
            {
                application.Context.Handler = new GateHandler();
            }
        }
    }
}
