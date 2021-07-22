#if ENABLE_INPUT_SYSTEM

namespace BlackTundra.Foundation.Control {

    /// <summary>
    /// Interface used to mark something as controllable by a single <see cref="ControlUser"/>.
    /// </summary>
    public interface IControllable {

        /// <summary>
        /// Invoked when control is gained.
        /// </summary>
        /// <param name="controlUser"><see cref="ControlUser"/> that is gaining control.</param>
        /// <returns>
        /// <see cref="ControlFlags"/> that should be set/unset after control is gained.
        /// </returns>
        ControlFlags OnControlGained(in ControlUser controlUser);

        /// <summary>
        /// Invoked when control is revoked from the <see cref="ControlUser"/> currently in
        /// control.
        /// </summary>
        /// <param name="controlUser"><see cref="ControlUser"/> that is losing control.</param>
        /// <returns>
        /// <see cref="ControlFlags"/> that should be set/unset after control is revoked.
        /// </returns>
        ControlFlags OnControlRevoked(in ControlUser controlUser);

    }

}

#endif