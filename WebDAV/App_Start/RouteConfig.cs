using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace WebDAV
{
    public class RouteConfig
    {
        public class HtmlConstraint : IRouteConstraint
        {
            public bool Match(HttpContextBase httpContext,
                Route route,
                string parameterName,
                RouteValueDictionary values,
                RouteDirection routeDirection)
            {
                try
                {
                    bool match = !string.IsNullOrEmpty(httpContext.Request.Headers["Accept"])
                        && httpContext.Request.HttpMethod.ToUpper() != "OPTIONS";
                    return match;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional },
                constraints: new { isHtml = new HtmlConstraint() }
            );
        }
    }
}
