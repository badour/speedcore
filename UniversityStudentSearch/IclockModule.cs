using System;
using System.Web;

namespace UniversityStudentSearch
{
    public sealed class IclockModule : IHttpModule
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
            HttpRequest request = application.Request;
            if (!IsIclock(request.Path))
            {
                return;
            }

            application.Context.Handler = new IclockHandler();
        }

        private static bool IsIclock(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path.Equals("/iclock", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/iclock/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
