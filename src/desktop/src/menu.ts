import type { MenuItemConstructorOptions } from "electron";
import { ru } from "./strings.js";

/** The menu bar of the app: none on Windows and Linux, where the SPA is the whole window. A Mac
 *  always shows one, and without its «Правка» Cmd+C, Cmd+V and Cmd+A do nothing in the fields,
 *  without the app's menu Cmd+Q does not quit. */
export function applicationMenu(platform: NodeJS.Platform): MenuItemConstructorOptions[] | null {
  if (platform !== "darwin") return null;
  return [
    {
      label: ru.appName,
      submenu: [
        { role: "about", label: ru.menuAbout },
        { type: "separator" },
        { role: "hide", label: ru.menuHide },
        { role: "hideOthers", label: ru.menuHideOthers },
        { role: "unhide", label: ru.menuUnhide },
        { type: "separator" },
        { role: "quit", label: ru.menuQuit },
      ],
    },
    {
      label: ru.menuEdit,
      submenu: [
        { role: "undo", label: ru.menuUndo },
        { role: "redo", label: ru.menuRedo },
        { type: "separator" },
        { role: "cut", label: ru.menuCut },
        { role: "copy", label: ru.menuCopy },
        { role: "paste", label: ru.menuPaste },
        { role: "selectAll", label: ru.menuSelectAll },
      ],
    },
    {
      label: ru.menuWindow,
      submenu: [
        { role: "minimize", label: ru.menuMinimize },
        { role: "zoom", label: ru.menuZoom },
        { role: "togglefullscreen", label: ru.menuFullScreen },
        { type: "separator" },
        { role: "close", label: ru.menuClose },
      ],
    },
  ];
}
