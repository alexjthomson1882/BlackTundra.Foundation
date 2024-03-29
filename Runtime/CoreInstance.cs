using UnityEngine;

namespace BlackTundra.Foundation {

    /// <summary>
    /// <see cref="MonoBehaviour"/> class responsible for initialising the <see cref="Core"/> static class.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(int.MinValue)]
    sealed class CoreInstance : MonoBehaviour {

        private void Start() => Core.OnInstanceStart();
        private void OnDestroy() => Core.Quit(QuitReason.CoreDestroyed);
        private void Update() => Core.Update();
        private void OnGUI() => Core.OnGUI();

    }

}
