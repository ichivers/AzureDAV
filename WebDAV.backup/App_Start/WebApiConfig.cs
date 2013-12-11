using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Routing;

namespace WebDAV
{
    public class DavConstraint : IRouteConstraint
    {
        public bool Match(HttpContextBase httpContext,
            Route route,
            string parameterName,
            RouteValueDictionary values,
            RouteDirection routeDirection)
        {
            try
            {
                bool match = string.IsNullOrEmpty(httpContext.Request.Headers["Accept"])
                    //|| httpContext.Request.Headers["Accept"].Contains("text/html") == false)
                    || httpContext.Request.HttpMethod.ToUpper() == "OPTIONS";
                return match;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            //config.Routes.MapHttpRoute(
            //    name: "DefaultApi",
            //    routeTemplate: "api/{controller}/{*data}",
            //    defaults: new { id = RouteParameter.Optional }
            //);

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "{*catchall}",
                defaults: new { controller = "wwwdavroot" },
                constraints : new { isDav = new DavConstraint() }
            );

            // Uncomment the following line of code to enable query support for actions with an IQueryable or IQueryable<T> return type.
            // To avoid processing unexpected or malicious queries, use the validation settings on QueryableAttribute to validate incoming queries.
            // For more information, visit http://go.microsoft.com/fwlink/?LinkId=279712.
            //config.EnableQuerySupport();

            // To disable tracing in your application, please comment out or remove the following line of code
            // For more information, refer to: http://www.asp.net/web-api
            config.EnableSystemDiagnosticsTracing();                  
        }
    }
}
