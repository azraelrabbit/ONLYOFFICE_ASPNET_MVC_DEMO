﻿/*
 *
 * (c) Copyright Ascensio System SIA 2019
 *
 * The MIT License (MIT)
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Script.Serialization;
using System.Web.Services;
using WebApplication1.Helpers;
using WebApplication1.Models;

namespace WebApplication1
{
    [WebService(Namespace = "http://tempuri.org/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    public class WebEditor : IHttpHandler
    {
        public void ProcessRequest(HttpContext context)
        {
            switch (context.Request["type"])
            {
                case "upload":
                    Upload(context);
                    break;
                case "convert":
                    Convert(context);
                    break;
                case "track":
                    Track(context);
                    break;
            }
        }

        private static void Upload(HttpContext context)
        {
            context.Response.ContentType = "text/plain";
            try
            {
                var httpPostedFile = context.Request.Files[0];
                string fileName;

                if (HttpContext.Current.Request.Browser.Browser.ToUpper() == "IE")
                {
                    var files = httpPostedFile.FileName.Split(new char[] { '\\' });
                    fileName = files[files.Length - 1];
                }
                else
                {
                    fileName = httpPostedFile.FileName;
                }

                var curSize = httpPostedFile.ContentLength;
                if (DocManagerHelper.MaxFileSize < curSize || curSize <= 0)
                {
                    throw new Exception("File size is incorrect");
                }

                var curExt = (Path.GetExtension(fileName) ?? "").ToLower();
                if (!DocManagerHelper.FileExts.Contains(curExt))
                {
                    throw new Exception("File type is not supported");
                }

                fileName = DocManagerHelper.GetCorrectName(fileName);

                var savedFileName = DocManagerHelper.StoragePath(fileName);
                httpPostedFile.SaveAs(savedFileName);

                context.Response.Write("{ \"filename\": \"" + fileName + "\"}");
            }
            catch (Exception e)
            {
                context.Response.Write("{ \"error\": \"" + e.Message + "\"}");
            }
        }

        private static void Convert(HttpContext context)
        {
            context.Response.ContentType = "text/plain";
            try
            {
                var fileName = context.Request["filename"];
                var fileUri = DocManagerHelper.GetFileUri(fileName);

                var extension = (Path.GetExtension(fileUri) ?? "").Trim('.');
                var internalExtension = DocManagerHelper.GetInternalExtension(FileUtility.GetFileType(fileName)).Trim('.');

                if (DocManagerHelper.ConvertExts.Contains("." + extension)
                    && !string.IsNullOrEmpty(internalExtension))
                {
                    var key = ServiceConverter.GenerateRevisionId(fileUri);

                    string newFileUri;
                    var result = ServiceConverter.GetConvertedUri(fileUri, extension, internalExtension, key, true, out newFileUri);
                    if (result != 100)
                    {
                        context.Response.Write("{ \"step\" : \"" + result + "\", \"filename\" : \"" + fileName + "\"}");
                        return;
                    }

                    var correctName = DocManagerHelper.GetCorrectName(Path.GetFileNameWithoutExtension(fileName) + "." + internalExtension);

                    var req = (HttpWebRequest)WebRequest.Create(newFileUri);

                    using (var stream = req.GetResponse().GetResponseStream())
                    {
                        if (stream == null) throw new Exception("Stream is null");
                        const int bufferSize = 4096;

                        using (var fs = File.Open(DocManagerHelper.StoragePath(correctName), FileMode.Create))
                        {
                            var buffer = new byte[bufferSize];
                            int readed;
                            while ((readed = stream.Read(buffer, 0, bufferSize)) != 0)
                            {
                                fs.Write(buffer, 0, readed);
                            }
                        }
                    }

                    File.Delete(DocManagerHelper.StoragePath(fileName));
                    fileName = correctName;
                }

                context.Response.Write("{ \"filename\" : \"" + fileName + "\"}");
            }
            catch (Exception e)
            {
                context.Response.Write("{ \"error\": \"" + e.Message + "\"}");
            }
        }

        private enum TrackerStatus
        {
            NotFound = 0,
            Editing = 1,
            MustSave = 2,
            Corrupted = 3,
            Closed = 4,
        }

        private static void Track(HttpContext context)
        {
            var userAddress = context.Request["userAddress"];
            var fileName = context.Request["fileName"];

            var storagePath = DocManagerHelper.StoragePath(fileName, userAddress);

            string body;
            try
            {
                using (var receiveStream = context.Request.InputStream)
                using (var readStream = new StreamReader(receiveStream))
                {
                    body = readStream.ReadToEnd();
                }
            }
            catch (Exception e)
            {
                throw new HttpException((int) HttpStatusCode.BadRequest, e.Message);
            }

            var jss = new JavaScriptSerializer();
            if (string.IsNullOrEmpty(body)) return;
            var fileData = jss.Deserialize<Dictionary<string, object>>(body);

            if (JwtManager.Enabled)
            {
                if (fileData.ContainsKey("token"))
                {
                    fileData = jss.Deserialize<Dictionary<string, object>>(JwtManager.Decode(fileData["token"].ToString()));
                }
                else if (context.Request.Headers.AllKeys.Contains("Authorization", StringComparer.InvariantCultureIgnoreCase))
                {
                    var headerToken = context.Request.Headers.Get("Authorization").Substring("Bearer ".Length);
                    fileData = (Dictionary<string, object>)jss.Deserialize<Dictionary<string, object>>(JwtManager.Decode(headerToken))["payload"];
                }
                else
                {
                    throw new Exception("Expected JWT");
                }
            }

            var status = (TrackerStatus) (int) fileData["status"];

            switch (status)
            {
                case TrackerStatus.MustSave:
                case TrackerStatus.Corrupted:
                    var downloadUri = (string) fileData["url"];

                    var req = (HttpWebRequest) WebRequest.Create(downloadUri);

                    var saved = 1;
                    try
                    {
                        using (var stream = req.GetResponse().GetResponseStream())
                        {
                            if (stream == null) throw new Exception("stream is null");
                            const int bufferSize = 4096;

                            using (var fs = File.Open(storagePath, FileMode.Create))
                            {
                                var buffer = new byte[bufferSize];
                                int readed;
                                while ((readed = stream.Read(buffer, 0, bufferSize)) != 0)
                                {
                                    fs.Write(buffer, 0, readed);
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        saved = 0;
                    }

                    break;
            }
            context.Response.Write("{\"error\":0}");
        }

        public bool IsReusable
        {
            get { return false; }
        }
    }
}