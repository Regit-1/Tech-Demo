using Steamworks;
using System;
using UnityEngine;

// Handles the initialization and runtime of the Steam client 
public class SteamManager : MonoBehaviour
{
    void Awake()
    {
        try {
            SteamClient.Init(480);
        }   catch (SystemException e) {
            Debug.Log("An error occurred initializing the steam client: " + e.Message);
        }

        DontDestroyOnLoad(this.gameObject);
    }

    private void Update() {
        SteamClient.RunCallbacks();
    }

    private void OnDisable() {
        SteamClient.Shutdown();
    }
}
