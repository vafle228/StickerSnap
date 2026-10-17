import { useEffect, useState } from "react";
import { Badge, Button, ConfirmDialog, Icon } from "@stickersnap/ds";
import { ApiError, isUnauthorized } from "../../api/client";
import { useDeleteSticker, useForgetPack, usePack } from "../../api/queries";
import type { PackDetail, Sticker } from "../../api/types";
import { useNav } from "../../app/nav";
import { Notice, Page } from "../../shared/Layout";
import layout from "../../shared/layout.module.css";
import { useToast } from "../../shared/toast";
import { useDelayed } from "../../shared/useDelayed";
import { useTelegram } from "../../telegram/context";
import { copyText } from "./clipboard";
import { countLabel, gridLabel, isFull, leftLabel, packLink, shareUrl } from "./format";
import styles from "./pack.module.css";
import { StickerSheet } from "./StickerSheet";

const COPIED_MS = 2000;

/** One pack (artboard 13, section 3): its link, «Добавить в Telegram», «Поделиться» and the
 *  grid of first frames; a tap on a sticker opens the sheet where it can be deleted. */
export function PackScreen({ name }: { name: string }) {
  const pack = usePack(name);
  const home = useNav((s) => s.home);
  const slow = useDelayed(pack.isPending);

  if (pack.isError) {
    const gone = pack.error instanceof ApiError && pack.error.status === 404;
    return (
      <Page label="Пак">
        {gone ? (
          <Notice
            tone="warning"
            title="Пак не найден"
            text="Его нет среди ваших паков в StickerSnap."
            action={
              <Button size="sm" onClick={home}>
                К моим пакам
              </Button>
            }
          />
        ) : (
          <Notice
            tone="danger"
            title="Не удалось загрузить пак"
            text="Нет связи с сервером StickerSnap."
            action={
              <Button size="sm" onClick={() => void pack.refetch()}>
                Повторить
              </Button>
            }
          />
        )}
      </Page>
    );
  }

  if (!pack.isSuccess) {
    return <Page label="Пак">{slow && <PackSkeleton />}</Page>;
  }
  return pack.data.state === "missing" ? (
    <MissingPack pack={pack.data} />
  ) : (
    <PackView pack={pack.data} />
  );
}

function PackHead({ pack }: { pack: PackDetail }) {
  const missing = pack.state === "missing";
  return (
    <div className={styles.head}>
      <span className={`${layout.cover} ${missing ? layout.dim : ""}`}>
        {pack.coverUrl && <img src={pack.coverUrl} alt="" />}
      </span>
      <div className={styles.headText}>
        <h1 className={styles.h2}>{pack.title}</h1>
        <div className={styles.badges}>
          {missing ? (
            <Badge tone="warning" icon="alert">
              Не найден
            </Badge>
          ) : (
            <>
              <Badge tone="primary">{countLabel(pack)}</Badge>
              {isFull(pack) && (
                <Badge tone="warning" icon="alert">
                  Заполнен
                </Badge>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}

/** Telegram no longer knows the pack (state 6): it can only leave our list. */
function MissingPack({ pack }: { pack: PackDetail }) {
  const telegram = useTelegram();
  const home = useNav((s) => s.home);
  const forget = useForgetPack();
  const show = useToast((s) => s.show);
  const remove = () =>
    forget.mutate(pack.name, {
      onSuccess: () => {
        telegram.haptic("done");
        home();
      },
      onError: (error) => {
        if (isUnauthorized(error)) return;
        telegram.haptic("error");
        show({
          tone: "danger",
          title: "Пак не убран",
          description: "Нет связи с сервером StickerSnap.",
        });
      },
    });
  return (
    <Page label="Пак">
      <PackHead pack={pack} />
      <Notice
        tone="warning"
        title="Пака нет в Telegram"
        text="Его удалили через @Stickers или Telegram его не нашёл."
        action={
          <Button size="sm" disabled={forget.isPending} onClick={remove}>
            Убрать из списка
          </Button>
        }
      />
    </Page>
  );
}

function PackView({ pack }: { pack: PackDetail }) {
  const telegram = useTelegram();
  const home = useNav((s) => s.home);
  const show = useToast((s) => s.show);
  const deletion = useDeleteSticker(pack.name);
  const [viewing, setViewing] = useState<number | null>(null);
  const [asking, setAsking] = useState<Sticker | null>(null);
  const [copied, setCopied] = useState(false);
  const link = packLink(pack);

  useEffect(() => {
    if (!copied) return undefined;
    const timer = window.setTimeout(() => setCopied(false), COPIED_MS);
    return () => window.clearTimeout(timer);
  }, [copied]);

  const copy = async () => {
    if (!(await copyText(`https://${link}`))) return;
    telegram.haptic("copied");
    setCopied(true);
  };

  const open = (index: number) => {
    telegram.haptic("select");
    setViewing(index);
  };

  const ask = (sticker: Sticker) => {
    telegram.haptic("confirm");
    setAsking(sticker);
  };

  const remove = (sticker: Sticker) => {
    const last = pack.stickers.length === 1;
    setAsking(null);
    setViewing(null);
    deletion.mutate(sticker.id, {
      onSuccess: () => {
        telegram.haptic("done");
        if (last) home();
        show({
          tone: "success",
          title: "Стикер удалён из пака",
          description: last
            ? "Пак исчез из Telegram вместе с ним."
            : leftLabel(pack.stickers.length - 1),
        });
      },
      onError: (error) => {
        if (isUnauthorized(error)) return;
        telegram.haptic("error");
        const wait =
          error instanceof ApiError && error.code === "rate_limited"
            ? error.problem?.params?.retryAfterSec
            : undefined;
        show({
          tone: "danger",
          title: "Стикер не удалён",
          description:
            wait !== undefined
              ? `Telegram просит подождать ${wait} с, стикер на месте.`
              : "Telegram не ответил, стикер на месте.",
          action: { label: "Повторить", run: () => remove(sticker) },
        });
      },
    });
  };

  const busy = deletion.isPending ? deletion.variables : undefined;
  const lastOne = pack.stickers.length === 1;

  return (
    <Page label="Пак">
      <PackHead pack={pack} />
      <button
        type="button"
        className={`${styles.link} ${copied ? styles.copied : ""}`}
        aria-label={copied ? undefined : `Скопировать ссылку ${link}`}
        aria-live="polite"
        onClick={() => void copy()}
      >
        {copied ? (
          <>
            <Icon name="check" size={18} strokeWidth={2} />
            <span>Скопировано</span>
          </>
        ) : (
          <>
            <span>{link}</span>
            <Icon name="copy" size={18} />
          </>
        )}
      </button>
      <div className={styles.actions}>
        <Button variant="primary" onClick={() => telegram.openTelegramLink(`https://${link}`)}>
          Добавить в Telegram
        </Button>
        <Button icon="share" onClick={() => telegram.openTelegramLink(shareUrl(pack))}>
          Поделиться
        </Button>
      </div>
      <div className={styles.gridHead}>{gridLabel(pack)}</div>
      <ul className={styles.grid} aria-label="Стикеры">
        {pack.stickers.map((sticker, i) => (
          <li key={sticker.id}>
            <button
              type="button"
              className={styles.tile}
              aria-label={`Стикер ${i + 1}: ${sticker.emoji.join(" ")}`}
              aria-busy={busy === sticker.id || undefined}
              disabled={busy === sticker.id}
              onClick={() => open(i)}
            >
              <span className={styles.box}>
                <img src={sticker.thumbUrl} alt="" loading="lazy" />
                {busy === sticker.id && (
                  <span className={styles.busy}>
                    <Icon name="spinner" size={22} />
                  </span>
                )}
              </span>
              <span className={styles.emoji}>{sticker.emoji.join(" ")}</span>
            </button>
          </li>
        ))}
      </ul>
      <p className={layout.foot}>Нажмите на стикер, чтобы посмотреть его ближе или удалить.</p>

      {viewing !== null && (
        <StickerSheet
          stickers={pack.stickers}
          index={Math.min(viewing, pack.stickers.length - 1)}
          onIndex={(index) => {
            telegram.haptic("select");
            setViewing(index);
          }}
          onClose={() => setViewing(null)}
          onDelete={ask}
        />
      )}
      <ConfirmDialog
        open={asking !== null}
        title="Удалить стикер из пака?"
        text={
          lastOne
            ? "Это последний стикер: пак исчезнет из Telegram вместе с ним."
            : "Он пропадёт у всех, кто добавил пак. Вернуть его можно, только опубликовав снова с компьютера."
        }
        confirmLabel={lastOne ? "Удалить пак" : "Удалить"}
        onConfirm={() => asking && remove(asking)}
        onCancel={() => setAsking(null)}
      />
    </Page>
  );
}

function PackSkeleton() {
  return (
    <>
      <div className={styles.head} aria-hidden>
        <span className={layout.skeleton} style={{ width: 56, height: 56, borderRadius: 14 }} />
        <span className={styles.headText}>
          <span className={layout.skeleton} style={{ width: "60%", height: 22 }} />
          <span className={layout.skeleton} style={{ width: 140, height: 24, borderRadius: 999 }} />
        </span>
      </div>
      <ul className={styles.grid} aria-hidden>
        {Array.from({ length: 8 }, (_, i) => (
          <li key={i}>
            <span
              className={`${layout.skeleton} ${styles.box}`}
              style={{ backgroundImage: "none" }}
            />
          </li>
        ))}
      </ul>
    </>
  );
}
