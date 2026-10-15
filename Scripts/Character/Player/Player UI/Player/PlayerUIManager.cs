using Unity.Netcode;
using UnityEngine;

public class PlayerUIManager : MonoBehaviour
{
    public static PlayerUIManager instance;

    [Header("NETWORK JOIN")]
    [SerializeField] bool startGameAsClient;

    private void Awake()
    {
        if (instance == null) instance = this;
        else Destroy(gameObject);
    }

    private void Start() => DontDestroyOnLoad(gameObject);

    private void Update()
    {
        if (!startGameAsClient) return;

        startGameAsClient = false;
        NetworkManager.Singleton.Shutdown();
        NetworkManager.Singleton.StartClient();
    }
}