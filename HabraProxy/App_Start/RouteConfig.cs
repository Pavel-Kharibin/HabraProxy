using System.Web.Mvc;
using System.Web.Routing;

namespace HabraProxy
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.MapRoute(
                name: "Default",
                url: "{*url}",
                defaults: new { controller = "Proxy", action = "Index", url = UrlParameter.Optional }
            );
        }
    }
}
