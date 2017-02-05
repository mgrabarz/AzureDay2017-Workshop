﻿using Contoso.Apps.SportsLeague.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Routing;
using System.Data.Entity;


namespace Contoso.Apps.SportsLeague.Offers
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            GlobalConfiguration.Configure(WebApiConfig.Register);

            // Initialize the product database.
            Database.SetInitializer(new ProductDatabaseInitializer());

        }
    }
}
