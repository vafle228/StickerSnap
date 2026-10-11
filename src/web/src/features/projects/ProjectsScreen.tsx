import { useEffect, useState, type KeyboardEvent } from "react";
import { Button, Card, DropZone, Icon, ProjectCard, type MenuItem } from "../../ds";
import { useApp } from "../../app/store";
import { mediaSrc } from "../../shared/api/client";
import { useAppInfo, useProjects, useSettings, useUpdateSettings } from "../../shared/api/queries";
import type { ProjectSummary } from "../../shared/api/types";
import { notifyProblem } from "../../shared/notify";
import { MenuPopover } from "../../shared/overlays";
import { platform } from "../../shared/platform";
import { t } from "../../shared/strings/ru";
import { useProjectActions } from "./actions";
import { useFileDrag } from "./drop";
import { durationChip, projectMeta } from "./format";
import styles from "./ProjectsScreen.module.css";
import { ReadingFile } from "./ReadingFile";
import { useImport } from "./reading";
import { RenameField } from "./RenameField";

type Action = "publish" | "rename" | "reveal" | "forget" | "trash";

/** Artboard 6: recent projects newest first, a new one from a video, a .ssproj from disk.
 *  Projects save themselves, so there is no «Сохранить». A click on a card opens it in the
 *  editor; everything else is in its «⋯» menu. Folders and paths exist only in the desktop app. */
export function ProjectsScreen() {
  const projects = useProjects();
  const desktop = useAppInfo().data?.mode === "desktop";
  const folder = useSettings().data?.folders.projects;
  const { mutate: updateSettings } = useUpdateSettings();
  const reading = useImport((s) => s.reading);
  const over = useFileDrag((s) => s.over);
  const actions = useProjectActions();
  const openProject = useApp((s) => s.openProject);
  const [menu, setMenu] = useState<{ id: string; anchor: HTMLElement } | null>(null);
  const [renaming, setRenaming] = useState<string>();

  useEffect(() => {
    if (projects.error) {
      notifyProblem(projects.error, {
        key: "projects",
        action: { label: t("common.retry"), onClick: () => void projects.refetch() },
      });
    }
  }, [projects.error, projects]);

  const changeFolder = async () => {
    if (!folder) return;
    const picked = await platform.pickFolder(folder);
    if (picked && picked !== folder) updateSettings({ folders: { projects: picked } });
  };

  const list = projects.data ?? [];
  const empty = projects.isSuccess && list.length === 0;
  const menuProject = list.find((p) => p.id === menu?.id);

  const items: (MenuItem<Action> | "-")[] = [
    // Publishing is stage 1c: off here, as the rainbow button in the editor's header is
    { value: "publish", label: t("projects.publish"), icon: "send", disabled: true },
    { value: "rename", label: t("projects.rename"), icon: "pencil", shortcut: "F2" },
    ...(desktop
      ? [{ value: "reveal" as const, label: t("projects.reveal"), icon: "folder" as const }]
      : []),
    "-",
    {
      value: "forget",
      label: t("projects.forget"),
      description: t("projects.forgetHint"),
      icon: "close",
    },
    { value: "trash", label: t("projects.trash"), icon: "trash", danger: true },
  ];

  const act = (action: Action, project: ProjectSummary) => {
    if (action === "rename") setRenaming(project.id);
    if (action === "reveal" && project.path) platform.showInFolder(project.path);
    if (action === "forget") void actions.forget(project);
    if (action === "trash") void actions.trash(project);
  };

  const onCardKey = (e: KeyboardEvent<HTMLElement>, project: ProjectSummary) => {
    if (e.key === "F2" && renaming !== project.id) {
      e.preventDefault();
      setRenaming(project.id);
    }
  };

  return (
    <div className={styles.screen}>
      <header className={styles.header}>
        <div className={styles.heading}>
          <h1 className={styles.title}>{t("nav.projects")}</h1>
          {desktop && folder && (
            <p className={styles.folder}>
              {t("projects.savedIn")} <span className={styles.path}>{folder}</span>
              {" · "}
              <button type="button" className={styles.link} onClick={() => void changeFolder()}>
                {t("projects.changeFolder")}
              </button>
            </p>
          )}
        </div>
        {/* With no projects the same two buttons stand in the empty state: one primary per screen */}
        {!empty && desktop && (
          <Button icon="folder" onClick={() => void actions.openFile()}>
            {t("projects.openFile")}
          </Button>
        )}
        {!empty && (
          <Button variant="primary" icon="plus" onClick={() => void actions.create()}>
            {t("projects.new")}
          </Button>
        )}
      </header>

      {empty ? (
        <Card className={styles.empty}>
          <span className={styles.emptyIcon} aria-hidden>
            <Icon name="folder" size={28} />
          </span>
          <h2 className={styles.emptyTitle}>{t("projects.empty.title")}</h2>
          <p className={styles.emptyText}>
            {t(desktop ? "projects.empty.text" : "projects.empty.textWeb")}
          </p>
          <div className={styles.emptyActions}>
            <Button variant="primary" onClick={() => void actions.create()}>
              {t("projects.new")}
            </Button>
            {desktop && (
              <Button variant="ghost" onClick={() => void actions.openFile()}>
                {t("projects.openFile")}
              </Button>
            )}
          </div>
        </Card>
      ) : (
        projects.isSuccess && (
          <>
            <p className={styles.count}>
              <b>{t("projects.recent", { count: list.length })}</b> {t("projects.order")}
            </p>
            <ul className={styles.grid} aria-label={t("projects.list")}>
              <li>
                {reading?.slow ? (
                  <ReadingFile className={styles.tile} />
                ) : (
                  <DropZone
                    variant="tile"
                    className={styles.tile}
                    title={t("projects.new")}
                    hint={t("projects.newHint")}
                    dropTitle={t("start.drop")}
                    busy={reading !== undefined}
                    dragging={over}
                    onClick={() => void actions.create()}
                    onFiles={(files) => files[0] && actions.openDropped(files[0])}
                  />
                )}
              </li>
              {list.map((p) => (
                <li key={p.id}>
                  <ProjectCard
                    className={styles.card}
                    name={p.name}
                    meta={projectMeta(p)}
                    duration={durationChip(p)}
                    cover={mediaSrc(p.coverUrl)}
                    missing={p.sourceStatus === "missing"}
                    sourceAction={{
                      label: t("projects.pickSource"),
                      title: p.path ? t("projects.pickSourceTitle", { path: p.path }) : undefined,
                      onClick: () => void actions.replaceSource(p.id),
                    }}
                    nameEditor={
                      renaming === p.id ? (
                        <RenameField
                          name={p.name}
                          onDone={(name) => {
                            setRenaming(undefined);
                            if (name !== undefined) void actions.rename(p, name);
                          }}
                        />
                      ) : undefined
                    }
                    openLabel={t("projects.open", { name: p.name })}
                    menuLabel={t("projects.menu")}
                    menuOpen={menu?.id === p.id}
                    onOpen={() => openProject(p.id)}
                    onMenu={(e) => {
                      const anchor = e.currentTarget;
                      setMenu((m) => (m?.id === p.id ? null : { id: p.id, anchor }));
                    }}
                    onKeyDown={(e) => onCardKey(e, p)}
                  />
                </li>
              ))}
            </ul>
          </>
        )
      )}

      <MenuPopover<Action>
        anchor={menu?.anchor ?? null}
        open={menuProject !== undefined}
        onClose={() => setMenu(null)}
        label={t("projects.menu")}
        menuClassName={styles.menu}
        items={items}
        onSelect={(action) => menuProject && act(action, menuProject)}
      />
    </div>
  );
}
