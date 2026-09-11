using UnityEngine;

namespace XRMultiplayer
{
    /// <summary>
    /// A very simple script that will enable or disable objects based on the Network Connection State.
    /// </summary>
    public class ConnectionToggler : MonoBehaviour
    {
        /// <summary>
        /// Enables all objects on connect.
        /// Disables all objects on disconnect.
        /// </summary>
        [SerializeField] GameObject[] objectsToEnableOnline;

        /// <summary>
        /// Enables all objects on disconnect.
        /// Disables all objects on connect.
        /// </summary>
        [SerializeField] GameObject[] objectsToEnableOffline;

        /// <inheritdoc/>
        void OnEnable()
        {
            XRINetworkGameManager.Connected.Subscribe(ToggleNetworkObjects);
            ToggleNetworkObjects(XRINetworkGameManager.Connected.Value);
            if (XRINetworkGameManager.Instance != null)
                XRINetworkGameManager.Instance.OnConnectionFailedAction += OnConnectionFailed;
        }

        /// <inheritdoc/>
        void OnDisable()
        {
            XRINetworkGameManager.Connected.Unsubscribe(ToggleNetworkObjects);
            if (XRINetworkGameManager.Instance != null)
                XRINetworkGameManager.Instance.OnConnectionFailedAction -= OnConnectionFailed;
        }

        void OnConnectionFailed(string reason)
        {
            ToggleNetworkObjects(false);
        }

        /// <summary>
        /// Toggles objects on or off based on whether or not connected.
        /// <see cref="m_Connected"/>
        /// </summary>
        /// <param name="online">
        /// Whether or not players are connected to a networked game.
        /// </param>
        protected virtual void ToggleNetworkObjects(bool online)
        {
            foreach (GameObject g in objectsToEnableOnline)
            {
                if (g == null) continue;
                g.SetActive(online);
            }

            foreach (GameObject g in objectsToEnableOffline)
            {
                if (g == null) continue;
                g.SetActive(!online);
            }
        }
    }
}
