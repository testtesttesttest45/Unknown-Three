using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class MainMenuCleanUp : MonoBehaviour
{


    private void Awake()
    {
        SessionManager.CleanUpSession();
    }

}