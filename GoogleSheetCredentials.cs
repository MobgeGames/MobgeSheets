using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using UnityEngine;

namespace Mobge.Sheets {
    public class GoogleSheetCredentials : ScriptableObject {
        public const string c_defaultAssetName = "GoogleSheetConfiguration";
        private static GoogleSheetCredentials _instance;
        public static GoogleSheetCredentials Instance {
            get {
                if(_instance == null) {
                    _instance = Resources.Load<GoogleSheetCredentials>(c_defaultAssetName);
                }
                return _instance;
            }
        }
        public static byte[] AccessGrantedPageContent {
            get {
                return Resources.Load<TextAsset>("GoogleAccessGrantedPage").bytes;
            }
        }
        public string[] scopes = {
            "https://www.googleapis.com/auth/drive",
	        "https://www.googleapis.com/auth/drive.file",
	        "https://www.googleapis.com/auth/spreadsheets"
        };
        public string clientId;
        public string clientSecret;
        [SerializeField] private string apiKey;
        public string defaultSheetId;
        public string APIKey => apiKey;
    }
}
