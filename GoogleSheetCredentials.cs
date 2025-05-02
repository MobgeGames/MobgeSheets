using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Mobge.Sheets {
    public class GoogleSheetCredentials : ScriptableObject {
        private static GoogleSheetCredentials _instance;
        public static GoogleSheetCredentials Instance {
            get {
                if(_instance == null) {
                    _instance = Resources.Load<GoogleSheetCredentials>("GoogleSheetSettings");
                }
                return _instance;
            }
        }
        // public string clientId;
        // public string clientSecret;
        // private string _accessToken;
        // private string _expirationData;
        // public async void EnsureAccessToken() {

        // }
        public string apiKey = "AIzaSyDcC2LaOt2HBb8wND1vuU9S37v9skfc2Ng";
    }
}
