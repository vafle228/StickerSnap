import { useEffect, useState } from "react";
import { Banner, Button, Card, Collapse, Icon } from "@stickersnap/ds";
import { useApp } from "../../app/store";
import { useAccount, useAppInfo, usePacks } from "../../shared/api/queries";
import { notifyProblem } from "../../shared/notify";
import { t } from "../../shared/strings/ru";
import { AddByLinkDialog } from "./AddByLinkDialog";
import { PackList } from "./PackList";
import { PackView } from "./PackView";
import styles from "./PacksScreen.module.css";
import { PublishDialog } from "./PublishDialog";
import { usePacksUi } from "./store";

/** Artboard 7: the packs the app made on the left, the open pack as a grid of stickers on the
 *  right. Packs are born with their first stickers, so «Создать пак» opens the publish window;
 *  one made before, or on another computer, comes back by «Добавить по ссылке…». */
export function PacksScreen() {
  const packs = usePacks();
  const account = useAccount().data;
  const bot = useAppInfo().data?.botUsername ?? "StickerSnapAppBot";
  const setSection = useApp((s) => s.setSection);
  const open = usePacksUi((s) => s.open);
  const openPack = usePacksUi((s) => s.openPack);
  const startPublish = usePacksUi((s) => s.startPublish);
  const [byLink, setByLink] = useState(false);

  useEffect(() => {
    if (packs.error) {
      notifyProblem(packs.error, {
        key: "packs",
        action: { label: t("common.retry"), onClick: () => void packs.refetch() },
      });
    }
  }, [packs.error, packs]);

  const list = packs.data ?? [];
  const empty = packs.isSuccess && list.length === 0;
  const current = list.find((p) => p.name === open) ?? list[0];
  const linked = account?.state === "linked" ? account.user : undefined;
  const unlinked = account?.state === "unlinked" || account?.state === "pending";
  const create = () => startPublish({ newPack: true });

  return (
    <div className={`${styles.screen} ss-section`}>
      <header className={styles.header}>
        <div className={styles.heading}>
          <h1 className={styles.title}>{t("nav.packs")}</h1>
          <p className={styles.account}>
            <span className={styles.dot} data-on={Boolean(linked)} aria-hidden />
            {linked ? (
              <>
                {t("packs.account")}{" "}
                <b>{linked.username ? `@${linked.username}` : linked.firstName}</b>
                {" · "}
                {t("packs.accountBot", { bot })}
              </>
            ) : (
              t("packs.accountNone", { bot })
            )}
            {" · "}
            <button type="button" className={styles.link} onClick={() => setSection("settings")}>
              {t("packs.accountSettings")}
            </button>
          </p>
        </div>
        <Button icon="link" onClick={() => setByLink(true)}>
          {t("packs.addByLink")}
        </Button>
        {!empty && (
          <Button variant="primary" icon="plus" onClick={create}>
            {t("packs.create")}
          </Button>
        )}
      </header>

      <Collapse open={unlinked}>
        <Banner
          tone="warning"
          title={t("packs.unlinked.title")}
          description={t("packs.unlinked.text", { bot })}
          action={
            <Button size="sm" onClick={() => setSection("settings")}>
              {t("packs.unlinked.action")}
            </Button>
          }
        />
      </Collapse>

      {empty ? (
        <Card className={styles.empty}>
          <span className={styles.emptyIcon} aria-hidden>
            <Icon name="sticker" size={28} />
          </span>
          <h2 className={styles.emptyTitle}>{t("packs.empty.title")}</h2>
          <p className={styles.emptyText}>{t("packs.empty.text")}</p>
          <div className={styles.emptyActions}>
            <Button variant="primary" onClick={create}>
              {t("packs.create")}
            </Button>
          </div>
        </Card>
      ) : (
        packs.isSuccess &&
        current && (
          <div className={styles.body}>
            <PackList packs={list} current={current.name} onOpen={openPack} />
            <PackView summary={current} />
          </div>
        )
      )}

      <PublishDialog />
      {byLink && <AddByLinkDialog onClose={() => setByLink(false)} />}
    </div>
  );
}
