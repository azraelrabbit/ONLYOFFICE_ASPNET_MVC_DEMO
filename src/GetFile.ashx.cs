using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Script.Serialization;
using WebApplication1.Helpers;

namespace WebApplication1
{
    /// <summary>
    /// MyEditor 的摘要说明
    /// </summary>
    public class GetFile : IHttpHandler
    {

        public void ProcessRequest(HttpContext context)
        {
            //context.Response.ContentType = "text/plain";
            //context.Response.Write("Hello World");

            if (context.Request.HttpMethod.ToLower() == "get")
            {
                //download
               
                var fileName = context.Request["filename"];
                var userHost = context.Request["userhost"];
                var filepath=DocManagerHelper.StoragePath(fileName,userHost);

                var ct=MimeMapping.GetMimeMapping(fileName);
                context.Response.ContentType = ct;

             
                context.Response.WriteFile(filepath);
            }
            else
            if (context.Request.HttpMethod.ToLower() == "post")
            {
                var fileName = context.Request["filename"];

                var filePath = DocManagerHelper.StoragePath(fileName);

                string body;
                using (var reader = new StreamReader(context.Request.InputStream))
                    body = reader.ReadToEnd();

                var fileData = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(body);
                if ((int) fileData["status"] == 2)
                {
                    var req = WebRequest.Create((string) fileData["url"]);

                    using (var stream = req.GetResponse().GetResponseStream())
                    using (var fs = File.Open(filePath, FileMode.Create))
                    {
                        var buffer = new byte[4096];
                        int readed;
                        while ((readed = stream.Read(buffer, 0, 4096)) != 0)
                            fs.Write(buffer, 0, readed);
                    }
                }
            }
            else
            {
                var methods = context.Request.HttpMethod;

                context.Response.ContentType = "text/plain";
                context.Response.Write("{\"error\":0}");
            }

        }

        public bool IsReusable
        {
            get
            {
                return false;
            }
        }
    }
}