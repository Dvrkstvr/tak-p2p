// D-051: a hidden tab (a locked phone, a sleeping laptop) can come back holding relay sockets that died without a close. When the
// tab becomes visible again, or the browser says it is back online, ask the game to probe its relays now rather than after the
// 60 s idle window. watch() is idempotent.
window.takVisibility = (() => {
  let target = null;
  const check = () => {
    if (target && document.visibilityState === "visible") {
      target.invokeMethodAsync("OnPageVisible").catch(() => { /* the app is gone or reloading */ });
    }
  };
  return {
    watch(dotnetRef) {
      const first = target === null;
      target = dotnetRef;
      if (first) {
        document.addEventListener("visibilitychange", check);
        window.addEventListener("online", check);
      }
    },
  };
})();
