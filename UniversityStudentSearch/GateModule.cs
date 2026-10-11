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
            string path = application.Request.Path ?? string.Empty;
            if (path.Equals("/gate", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/gate/", StringComparison.OrdinalIgnoreCase))
            {
                application.Context.Handler = new GateHandler();
            }
        }
    }
}
