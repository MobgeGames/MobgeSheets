using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mobge.Sheets;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Mobge.Sheets {
    public class GoogleAuthenticator {
        public const string c_authorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        public const string c_tokenEndpoint = "https://oauth2.googleapis.com/token";
        private static GoogleAuthenticator s_instance;
        private OAuthListener _activeListener;
        private string _settedCode;

        public static GoogleAuthenticator Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = new GoogleAuthenticator(GoogleSheetCredentials.Instance);
                }
                return s_instance;
            }
        }
        private AccessParams _accessParams;
        private GoogleSheetCredentials _credentials;
        public GoogleAuthenticator(GoogleSheetCredentials credentials) {
            _credentials = credentials;
            string sData = PlayerPrefs.GetString(SaveKey, null);
            if (!string.IsNullOrEmpty(sData))
            {
                _accessParams = JsonUtility.FromJson<AccessParams>(sData);
            }
        }
        private string SaveKey {
            get => "gglAcc" + _credentials.GetInstanceID();
        }
        private void SaveAccessParams() {
            string jPrms = JsonUtility.ToJson(_accessParams);
            // Debug.Log("access params save " + jPrms);
            PlayerPrefs.SetString(SaveKey, jPrms);
        } 
        public async Task<bool> TryAddAuthentication(UnityWebRequest req, bool forceUseApiKey = false) {
            if (Application.isEditor && !forceUseApiKey) {
                string token = await GetAccessToken();
                // return false if get access token fails
                if(string.IsNullOrEmpty(token)) {
                    return false;
                }
                req.SetRequestHeader("Authorization", _accessParams.tokenType + " " + _accessParams.accessToken);
            } else {
                var uri = new Uri(req.url);
                string separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
                req.url = uri + separator + "key=" + UnityWebRequest.EscapeURL(_credentials.APIKey);
            }
            return true;
        }
        public async Task<string> GetAccessToken() {
            long now = DateTime.Now.Ticks;
            long leftTime = _accessParams.expirationDate - now;
            if(leftTime < 0) {
                // Debug.Log("-----token expired");
                await RequestAccessToken();
                return _accessParams.accessToken;
            }
            long passedTime = now - _accessParams.startDate;
            if(new TimeSpan(passedTime).TotalSeconds > 60 && !string.IsNullOrEmpty(_accessParams.refreshToken)) {
                // Debug.Log("-----refresh token with key");
                await TryRefreshAccessToken("refresh_token", "refresh_token", _accessParams.refreshToken);
            }
            return _accessParams.accessToken;
        }
        
        public void SetUrlCode(string token)
        {
            _settedCode = token;
            if (_activeListener != null)
            {
                _activeListener.Stop();
            }
        }
        public async Task<bool> RequestAccessToken()
        {
            OAuthListener l = new();
            _activeListener = l;

            l.StartListening(300, out string redurectUri);
            // Debug.Log("redirect uri: " + redurectUri);

            string state = "mbs_" + new System.Random().Next(0, int.MaxValue).ToString();
            ExecuteAccessTokenRequest(state, redurectUri);
            var context = await l.GetContextAsync();
            if (context == null)
            {
                Debug.Log("Request Access Token: Timeout");
            }
            else
            {
                var outStream = context.Response.OutputStream;
                var outBytes = GoogleSheetCredentials.AccessGrantedPageContent;
                await outStream.WriteAsync(outBytes, 0, outBytes.Length);
                outStream.Close();
            }
            l.Stop();
            if (!TryGetCode(context, state, out string code))
            {
                code = _settedCode;
                _settedCode = null;
                if (string.IsNullOrEmpty(code))
                {
                    return false;
                }
            }
            
            return await TryRefreshAccessToken("authorization_code", "code", code, redurectUri);
        }
        private bool TryGetCode(HttpListenerContext context, string state, out string code)
        {
            code = default;
            if (context == null)
            {
                return false;
            }
            string error = context.Request.QueryString.Get("error");
            if (error is object)
            {
                Debug.LogError(error);
                return false;
            }
            code = context.Request.QueryString.Get("code");
            string responseState = context.Request.QueryString.Get("state");
            if (code is null || responseState is null)
            {
                Debug.LogError($"Malformed authorization response. {context.Request.QueryString}");
                return false;
            }
            if (responseState != state)
            {
                Debug.LogError("State mismatch");
                return false;
            }
            return true;
        }
        private void ExecuteAccessTokenRequest(string state, string redirectUri)
        {
            UriBuilder b = new();
            b.Reset(c_authorizationEndpoint);
            b.AddParameter("response_type", "code");
            b.AddParameter("access_type", "offline");
            b.AddParameter("include_granted_scopes", "true");
            b.AddParameter("client_id", _credentials.clientId);
            var scopes = _credentials.scopes;
            string scopesCombined = "";
            for (int i = 0; i < scopes.Length; i++)
            {
                string next = scopes[i];
                if (i > 0)
                {
                    next = "%20" + next;
                }
                scopesCombined += next;
            }
            b.AddParameter("scope", scopesCombined);
            b.AddParameter("redirect_uri", redirectUri);
            b.AddParameter("state", state);

#if UNITY_EDITOR
            System.Diagnostics.Process.Start(b.Uri);
#else
            Application.OpenURL(b.Uri);
#endif
        }
        public static int GetRandomUnusedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
        private async Task<bool> TryRefreshAccessToken(string grandType, string codeKey, string code, string redirectUri = null)
        {

            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(c_tokenEndpoint);
            r.Method = "POST";
            r.ContentType = "application/x-www-form-urlencoded";
            r.Accept = "Accept=text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
            UriBuilder b = new();
            b.AddParameter(codeKey, code);
            b.AddParameter("client_id", _credentials.clientId);
            b.AddParameter("client_secret", _credentials.clientSecret);
            b.AddParameter("grant_type", grandType);
            if (!string.IsNullOrEmpty(redirectUri))
            {
                b.AddParameter("redirect_uri", redirectUri);
            }
            // Debug.Log("payload: " + b.Uri);
            var bodyBytes = Encoding.UTF8.GetBytes(b.Uri);
            r.ContentLength = bodyBytes.Length;
            using (var stream = r.GetRequestStream())
            {
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
            }
            WebResponse response;
            try
            {
                response = await r.GetResponseAsync();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                _accessParams = default;
                SaveAccessParams();
                return false;
            }
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
            {
                var result = SimpleJSON.JSON.Parse(await reader.ReadToEndAsync());
                _accessParams.accessToken = result["access_token"];
                if (string.IsNullOrEmpty(_accessParams.accessToken))
                {
                    _accessParams = default;
                    SaveAccessParams();
                    return false;
                }
                // Debug.Log("result: " + result.ToString());
                _accessParams.refreshToken = result["refresh_token"];
                int expiresIn = (int)result["expires_in"].AsDouble;
                expiresIn -= 10;
                var now = DateTime.Now;
                long dNow = now.Ticks;
                _accessParams.expirationDate = (DateTime.Now + TimeSpan.FromSeconds(expiresIn)).Ticks;
                _accessParams.tokenType = result["token_type"];
                _accessParams.startDate = dNow;
                SaveAccessParams();
            }
            Debug.Log("Access Token Refreshed");
            return true;
        }


        private class OAuthListener {
            public HttpListener listener = new();
            private CancellationTokenSource _timeoutCancel;

            public void StartListening(int timeOutSeconds, out string uri) {
                int port = GetRandomUnusedPort();
                port = 49710;
                string domain = IPAddress.Loopback.ToString();
                domain = "localhost";

                uri = $"http://{domain}:{port}/";
                listener.Prefixes.Add(uri);
                listener.Start();
                StartTimeOut(timeOutSeconds);
            }
            public async Task<HttpListenerContext> GetContextAsync() {
                try{
                    var result = await listener.GetContextAsync();
                    _timeoutCancel.Cancel();
                    return result;
                }
                catch(Exception e) {
                    Debug.Log("Timeout finished: " + e);
                }
                return null;
            }
            public void Stop() {
                listener.Stop();
            }

            private async void StartTimeOut(int timeOutSeconds) {
                _timeoutCancel = new CancellationTokenSource();
                try {
                    await Task.Delay(timeOutSeconds * 1000, _timeoutCancel.Token);
                    if(listener != null && listener.IsListening) {
                        listener.Stop();
                    }
                }
                catch {}
            }
        }
        [Serializable]
        public struct AccessParams {
            public string accessToken;
            public string refreshToken;
            public string tokenType;
            public long expirationDate;
            public long startDate;
        }

    }
}
