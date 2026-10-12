// One tab per game (M0 code review, finding 6). Every tab of this origin shares the same key, so two tabs playing one game could
// sign two different moves for one turn. Same idea as the CLI's profile lock. A Web Lock is held until release() or until the tab
// goes away (the browser frees it then).
window.takGameLock = (() => {
  const held = new Map();
  return {
    // "held" (this tab has it), "busy" (another tab has it) or "unsupported" (no Web Locks here: allowed, with a warning).
    acquire(name) {
      if (held.has(name)) return Promise.resolve("held");
      if (!(navigator.locks && typeof navigator.locks.request === "function")) {
        console.warn(`Tak: the Web Locks API is missing (an http:// page on another host?), so nothing stops a second tab from playing ${name} at the same time.`);
        return Promise.resolve("unsupported");
      }
      return new Promise((resolve) => {
        navigator.locks.request(name, { ifAvailable: true }, (lock) => {
          if (!lock) {
            resolve("busy");
            return undefined;
          }
          return new Promise((release) => {
            held.set(name, release);
            resolve("held");
          });
        }).catch((err) => {
          console.warn(`Tak: the Web Lock for ${name} failed (${err}); playing without it.`);
          resolve("unsupported");
        });
      });
    },
    release(name) {
      const release = held.get(name);
      if (release) {
        held.delete(name);
        release();
      }
    },
  };
})();
