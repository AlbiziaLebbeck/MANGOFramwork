mergeInto(LibraryManager.library, {
  MangosAcquirePointerLock: function () {
    const canvas = Module.canvas;
    if (!canvas) {
      return;
    }

    window.mangosPointerLockState = window.mangosPointerLockState || {};
    const state = window.mangosPointerLockState;

    const cleanup = function () {
      if (state.handler) {
        canvas.removeEventListener("pointerdown", state.handler, true);
        document.removeEventListener("keydown", state.handler, true);
      }
      if (state.pointerLockChanged) {
        document.removeEventListener("pointerlockchange", state.pointerLockChanged, true);
      }
      if (state.pointerLockError) {
        document.removeEventListener("pointerlockerror", state.pointerLockError, true);
      }
      state.handler = null;
      state.pointerLockChanged = null;
      state.pointerLockError = null;
    };

    const armNextGesture = function () {
      cleanup();

      state.handler = function () {
        requestLock();
      };
      state.pointerLockChanged = function () {
        if (document.pointerLockElement === canvas) {
          cleanup();
        }
      };

      canvas.addEventListener("pointerdown", state.handler, true);
      document.addEventListener("keydown", state.handler, true);
      document.addEventListener("pointerlockchange", state.pointerLockChanged, true);
    };

    const requestLock = function () {
      cleanup();

      if (document.pointerLockElement === canvas) {
        return;
      }

      state.pointerLockChanged = function () {
        if (document.pointerLockElement === canvas) {
          cleanup();
        }
      };
      state.pointerLockError = function () {
        armNextGesture();
      };
      document.addEventListener("pointerlockchange", state.pointerLockChanged, true);
      document.addEventListener("pointerlockerror", state.pointerLockError, true);

      try {
        const result = canvas.requestPointerLock();
        if (result && typeof result.catch === "function") {
          result.catch(function () {
            armNextGesture();
          });
        }
      } catch (error) {
        armNextGesture();
      }
    };

    if (document.pointerLockElement === canvas) {
      cleanup();
      return;
    }

    if (navigator.userActivation && navigator.userActivation.isActive) {
      requestLock();
    } else {
      armNextGesture();
    }
  },

  MangosReleasePointerLock: function () {
    const canvas = Module.canvas;
    const state = window.mangosPointerLockState;

    if (state) {
      if (state.handler) {
        canvas.removeEventListener("pointerdown", state.handler, true);
        document.removeEventListener("keydown", state.handler, true);
      }
      if (state.pointerLockChanged) {
        document.removeEventListener("pointerlockchange", state.pointerLockChanged, true);
      }
      if (state.pointerLockError) {
        document.removeEventListener("pointerlockerror", state.pointerLockError, true);
      }
      state.handler = null;
      state.pointerLockChanged = null;
      state.pointerLockError = null;
    }

    if (document.pointerLockElement === canvas && document.exitPointerLock) {
      document.exitPointerLock();
    }
  }
});
