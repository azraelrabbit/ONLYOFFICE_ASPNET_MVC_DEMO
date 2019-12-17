using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using WebApplication1.Helpers;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult Editor(string fileName, string mode)
        {
            mode = mode ?? string.Empty;

            var file = new FileModel
            {
                TypeDesktop = mode != "embedded",
                FileName = fileName
            };

            return View("Editor", file);
        }

        public ActionResult Sample(string fileExt)
        {
            var fileName = DocManagerHelper.CreateDemo(fileExt);
            Response.Redirect(Url.Action("Editor", "Home", new { fileName = fileName }));
            return null;
        }

        public ActionResult GetFile(string fileName)
        {
            var filepath=DocManagerHelper.StoragePath(fileName);
            return File(filepath,"text/plain");

        }

         
    }
}