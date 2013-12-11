using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Routing;

namespace WebDAV
{
    public static class WebApiConfig
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
                        || httpContext.Request.HttpMethod.ToUpper() == "OPTIONS";
                    return match;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static void Register(HttpConfiguration config)
        {
            // Web API configuration and services

            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "{*catchall}",
                defaults: new { controller = "wwwdavroot" },
                constraints: new { isDav = new DavConstraint() }
            );
        }
    }
}
