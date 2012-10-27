using System;
using System.Diagnostics;
using System.IO;
using System.Web;
using Ninject;

namespace NuGetGallery.FileAsyncUpload
{
    public class AsyncFileUploadModule : IHttpModule
    {
        private IUploadFileService _uploadFileService;

        public void Dispose()
        {
        }

        public void Init(HttpApplication application)
        {
            _uploadFileService = Container.Kernel.Get<IUploadFileService>();
            Debug.Assert(_uploadFileService != null);

            if (_uploadFileService != null)
            {
                application.PostAuthenticateRequest += PostAuthorizeRequest;
            }
        }

        private void PostAuthorizeRequest(object sender, EventArgs e)
        {
            var app = sender as HttpApplication;

            if (!app.Context.User.Identity.IsAuthenticated)
            {
                return;
            }

            if (!IsAsyncUploadRequest(app.Context))
            {
                return;
            }

            var username = app.Context.User.Identity.Name;
            if (String.IsNullOrEmpty(username))
            {
                return;
            }

            HttpRequest request = app.Context.Request;
            string contentType = request.ContentType;
            int boundaryIndex = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
            string boundary = "--" + contentType.Substring(boundaryIndex + 9);
            var requestParser = new FileUploadRequestParser(boundary, request.ContentEncoding);

            var progress = new AsyncFileUploadProgressDetails(request.ContentLength, 0, String.Empty);
            _uploadFileService.SetProgressDetails(username, progress);

            if (request.ReadEntityBodyMode != ReadEntityBodyMode.None)
            {
                return;
            }

            Stream uploadStream = request.GetBufferedInputStream();
            Debug.Assert(uploadStream != null);

            ReadStream(uploadStream, username, progress, requestParser);
        }

        private void ReadStream(
            Stream stream, 
            string userKey,
            AsyncFileUploadProgressDetails progress, 
            FileUploadRequestParser parser)
        {
            const int bufferSize = 1024 * 4; // in bytes

            var buffer = new byte[bufferSize];
            while (progress.BytesRemaining > 0)
            {
                int bytesRead = stream.Read(buffer, 0, Math.Min(progress.BytesRemaining, bufferSize));
                int newBytesRead = bytesRead == 0
                                    ? progress.TotalBytes
                                    : (progress.BytesRead + bytesRead);

                string newFileName = progress.FileName;

                if (bytesRead > 0)
                {
                    parser.ParseNext(buffer, bytesRead);
                    newFileName = parser.CurrentFileName;
                }

                progress = new AsyncFileUploadProgressDetails(progress.TotalBytes, newBytesRead, newFileName);
                _uploadFileService.SetProgressDetails(userKey, progress);

#if DEBUG
                // for demo purpose only
                System.Threading.Thread.Sleep(500);
#endif
            }
        }

        private static bool IsAsyncUploadRequest(HttpContext context)
        {
            // not a POST request
            if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // not a multipart content type
            string contentType = context.Request.ContentType;
            if (!contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            // Don't deal with transfer-encoding-chunked and less than 4KB
            if (context.Request.ContentLength < 4096)
            {
                return false;
            }

            return true;
        }
    }
}