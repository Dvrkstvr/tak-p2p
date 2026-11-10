# Board gestures (Blazor `/play` and `/online`) — state/transition table

> **Redesign 2026-11-10 (D-052).** The gestures below now live in one place, `src/TakApp.Blazor/Services/MoveInput.cs`, which the
> shared game screen (`Components/Game/GameTable.razor`) uses for local, vs AI and online games, on the 2D board (`Board2D.razor`) and
> the 3D board (`Board3D.razor` + `wwwroot/js/board3d.js`). The rows keep their meaning; only the wording on screen changed:
> the opening reads `Opening · White places a black flat`, play reads `White to move` (or `Your move` against the AI or online), a
> result reads `<Name> wins by road` (`You win by road`, which also fixes Q-2). The inspector is an HTML panel (`.stack-inspector`)
> over the board in both views, the error box is `.table-error` above the board, the reserve chips sit on the player cards
> (`.player-card.is-turn`). `tests/e2e/play.mjs` drives these rows on the 2D board, then on the 3D board; `tests/e2e/online.mjs`
> drives them in an online game. Rows G-08, G-19 and G-53..G-56 are new with the redesign.

Characterisation of what the page does today (F-043, M1 design task; scope.md "Stepped tower movement gesture"). Read from
`src/TakApp.Blazor/Pages/Play.razor`, `Components/Board/TakBoardView.razor`, `PieceInventoryView.razor`, `StackSlideBar.razor`
and confirmed by driving the page with `node tests/e2e/play.mjs` (2026-10-12; the same run passes after F-041 moved the page onto
`TakGameSession`). This describes behaviour, it does not propose any:
M1 freezes it (D-006 spirit). Rows marked *driven* are checked by the Playwright script, which cites the row id in its log.

## Page state
- **Selected piece**: Flat / Wall / Cap. Starts as Flat when the page opens; survives moves (it is not reset after a placement).
- **Mode**: `Idle`, `Inspecting(square)`, `Slide(origin, carry, max)` with no direction yet, or `Stepping(origin, dir, drops)`.
  `max = min(stack height, board size)`; in hand = carry − sum(drops).
- **Game**: Opening (each side places one opponent flat), Playing, or Completed. While the AI is thinking, board clicks do nothing.
- Every move goes to the engine; a refusal is shown in the red error box (left column on desktop, above the board on mobile) with the
  engine's message. The next square click clears the box.

## Piece selection
| id | in state | gesture | result | driven |
|---|---|---|---|---|
| G-01 | page opens | — | Selected = Flat; every empty square carries the placement marker (`legal-target`); opening status `OPENING: WHITE PLACES OPPONENT` | yes |
| G-02 | any, active player's card | left-click Flat chip | Flat selected → Wall; anything else → Flat. Chips of the side not to move do nothing | yes |
| G-03 | any, active player's card | right-click Flat chip | Wall | yes |
| G-04 | any | click Wall / Cap chip or the Flat·Wall·Cap tab under the board | that piece; placement markers show only squares where the engine allows that piece (none for a Wall or Cap in the opening). Cap chip is disabled at 0 caps; the Cap tab is hidden on 4×4 | yes (tab) |
| G-05 | any | key F / W / C | Flat / Wall / Cap (C ignored on 4×4). Selecting a piece also ends a Slide/Stepping and closes the inspector | yes (F) |
| G-06 | any | key Esc | back to Idle (ends a move, closes the inspector and the controls guide) | yes |
| G-07 | any | key ? or H | toggles the controls guide | no |
| G-08 | any | key V, or the 2D/3D switch in the top bar | switches between the 2D and the 3D board; the game and any half-built move carry over. The choice is kept in localStorage (`tak.view`); 3D is the default, 2D when the browser has no WebGL | yes |

## Board, Idle
| id | in state | gesture | result | driven |
|---|---|---|---|---|
| G-10 | Idle, Opening/Playing | left-click empty square | place the selected piece there (Opening: an opponent flat). Success: last-move marker on it | yes |
| G-10b | Idle | left-click empty square, engine refuses (e.g. Wall in the opening, no reserves) | nothing changes; error box shows the engine message | yes (opening wall) |
| G-11 | Idle, Playing | right-click empty square | place a Wall regardless of the selected piece; selection unchanged. In the opening the engine refuses (G-10b) | yes |
| G-12 | Idle, Playing | left-click own stack (top piece is the mover's) | Slide(origin, carry = max). Origin highlighted with an `xN` badge; HUD "Moving X: N of M tiles carried"; enterable neighbours get the slide marker; the slide bar opens | yes |
| G-13 | Idle | left-click a stack that is not the mover's (or any stack in the opening) | Inspecting(square): overlay lists the stack top to bottom | yes |
| G-14 | Idle | right-click a stack | Inspecting(square) | no |
| G-15 | Inspecting | left-click a square | closes the inspector, then the click is handled as in Idle | yes |
| G-16 | Inspecting | right-click anywhere, or click the overlay | closes the inspector only | no |
| G-17 | Completed | left-click empty square | engine refuses ("Game is already completed."), error shown, nothing changes. No markers are drawn | yes |
| G-18 | any | click the board background (outside the squares) | back to Idle | no |
| G-19 | any, 3D board | drag / wheel / pinch; touch long-press | drag orbits the camera, wheel or pinch zooms (a press that moves more than 7 px is not a click); a touch long-press is a right-click (G-11, G-14, G-25). ⟲ resets the camera, ⊡ looks straight down | yes (clicks, right-click) |

## Board, Slide (origin picked, no direction yet)
| id | in state | gesture | result | driven |
|---|---|---|---|---|
| G-20 | Slide | left-click the origin | carry − 1 (one more tile stays behind); below 1 wraps to max | yes |
| G-21 | Slide | left-click an orthogonal neighbour the carried stack may enter | Stepping: direction set, 1 tile dropped there. If carry was 1 the move is sent at once | yes |
| G-22 | Slide | left-click another own stack (incl. a neighbour it may not enter) | Slide restarts at that stack | no |
| G-23 | Slide | left-click any other square | move cancelled, nothing placed, back to Idle | yes (G-24 in the script) |
| G-24 | Slide | left-click out of range (alias of G-23, the case the script drives) | as G-23 | yes |
| G-25 | Slide/Stepping | right-click anywhere | move cancelled, back to Idle | yes (G-33) |

"May enter": empty, a flat on top, or a wall on top when the last tile in hand is a capstone travelling alone. Capstones block.

## Board, Stepping (direction chosen)
| id | in state | gesture | result | driven |
|---|---|---|---|---|
| G-30 | Stepping, tiles in hand | left-click the current step square (not a wall) | one more tile dropped there (`slide-cur-target`); when the hand is empty the move is sent | yes |
| G-31 | Stepping, tiles in hand | left-click the next square along the direction, if it may be entered | 1 tile dropped there (`slide-next-target`); when the hand is empty the move is sent | yes |
| G-32 | Stepping | left-click anything else (origin included) | whole move cancelled, nothing placed, back to Idle | yes |
| G-33 | Stepping | right-click | whole move cancelled | yes |

A sent move that the engine refuses leaves the board unchanged and shows the error (G-10b).

## Slide bar (below the board while a move is started)
| id | in state | gesture | result | driven |
|---|---|---|---|---|
| G-40 | Slide/Stepping | — | shows direction buttons (only directions with a legal move are enabled) and every legal drop split for the chosen direction, from the engine's move list | yes (opens) |
| G-41 | Slide/Stepping | ✕ Cancel | back to Idle | yes |
| G-42 | Slide/Stepping | pick direction, pick split, Confirm Move | sends that move | no (see quirk Q-1) |
| G-43 | Slide/Stepping, new origin | — | the slide bar shows that stack's first direction and split; nothing is kept from an earlier origin | yes |

## Game end and AI
| id | in state | gesture | result | driven |
|---|---|---|---|---|
| G-50 | a move completes a road | — | status `<NAME> WINS (ROAD)`, road squares outlined, Resign hidden, no markers | yes |
| G-51 | Opening/Playing | Resign | the side to move resigns; status `<NAME> WINS (RESIGNATION)` | no |
| G-52 | vs AI, after the human's move | — | "AI is thinking…" for ~350 ms, then the AI's move is applied; the game runs to a result. With the human as Black the AI opens and the board faces Black | yes |
| G-53 | online or vs AI, the other side to move | click / right-click | no tray, no markers; nothing is sent; a click on a stack opens the inspector | yes (online) |
| G-54 | game over | Play again (local and vs AI) | a fresh game with the same size, komi, players, difficulty and colours | yes |
| G-55 | Opening/Playing | Resign | asks "Really resign?" first; a second click resigns | yes |
| G-56 | online, Black's device | — | the board (2D and 3D) and the player list are turned to face Black | yes |

## Quirks seen while characterising (kept as they are in M1)
- Q-1 (fixed in M1, G-43, D-050): the slide bar kept its chosen direction and split from an earlier origin until its own Cancel/Confirm is used, so after a
  board-click move its Confirm button can name a move of another square (seen: "Confirm Move (b2+)" while b3 is the origin).
  Confirm sends that stale move to the engine, which refuses it if illegal. Not driven by the script.
- Q-2 (fixed in the redesign): the vs-AI result line read "YOU WINS (ROAD)"; it now reads "You win by road".
- Q-3 (gone with the redesign): the mobile inventory line showed the stones count for both F and W; phones now show the player cards.
