import { Badge, Button, Icon } from "@stickersnap/ds";
import appIcon from "../../assets/app-icon.png";
import { useDevices, usePacks } from "../../api/queries";
import type { Pack } from "../../api/types";
import { useNav } from "../../app/nav";
import { DOWNLOAD_URL } from "../../app/links";
import { Empty, Notice, Page, PageHead, SkeletonRows } from "../../shared/Layout";
import layout from "../../shared/layout.module.css";
import { useDelayed } from "../../shared/useDelayed";
import { useTelegram } from "../../telegram/context";
import { devicesLabel } from "../devices/format";
import { countLabel, isFull } from "./format";

/** «Мои паки», the first screen (artboard 13, section 2): only the packs our bot made for this
 *  Telegram account, fresh first, and the card of the linked computers. */
export function PacksScreen() {
  const telegram = useTelegram();
  const packs = usePacks();
  const devices = useDevices();
  const push = useNav((s) => s.push);
  const slow = useDelayed(packs.isPending);

  if (packs.isSuccess && packs.data.length === 0) {
    return (
      <Page label="Мои паки">
        <Empty
          art={<img className={layout.appIcon} src={appIcon} alt="StickerSnap" />}
          title="Паков пока нет"
          text="Стикеры делаются в StickerSnap на компьютере. Опубликуйте первый, и пак появится здесь."
          action={
            <Button
              variant="primary"
              icon="download"
              onClick={() => telegram.openLink(DOWNLOAD_URL)}
            >
              Скачать StickerSnap
            </Button>
          }
        />
      </Page>
    );
  }

  return (
    <Page label="Мои паки">
      <PageHead title="Мои паки" sub="Паки, которые вы сделали в StickerSnap" />
      {packs.isError && (
        <Notice
          tone="danger"
          title="Не удалось загрузить паки"
          text="Нет связи с сервером StickerSnap."
          action={
            <Button size="sm" onClick={() => void packs.refetch()}>
              Повторить
            </Button>
          }
        />
      )}
      {packs.isSuccess ? (
        <>
          <ul className={layout.list} aria-label="Паки">
            {packs.data.map((pack) => (
              <li key={pack.name}>
                <PackRow pack={pack} onOpen={() => push({ kind: "pack", name: pack.name })} />
              </li>
            ))}
          </ul>
          <button
            type="button"
            className={`${layout.row} ${layout.solo}`}
            onClick={() => push({ kind: "devices" })}
          >
            <span className={layout.deviceIcon}>
              <Icon name="computer" size={22} />
            </span>
            <span className={layout.text}>
              <span className={layout.title}>Устройства</span>
              <span className={layout.meta}>
                {devices.isSuccess ? devicesLabel(devices.data.length) : "Привязанные компьютеры"}
              </span>
            </span>
            <Icon name="chevron-right" size={18} strokeWidth={2} className={layout.chevron} />
          </button>
          <p className={layout.foot}>
            Стикеры делаются в StickerSnap на компьютере. Здесь их смотрят, отправляют друзьям и
            удаляют.
          </p>
        </>
      ) : (
        (slow || packs.isError) && (
          <SkeletonRows count={packs.isError ? 4 : 5} faded={packs.isError} />
        )
      )}
    </Page>
  );
}

function PackRow({ pack, onOpen }: { pack: Pack; onOpen: () => void }) {
  const missing = pack.state === "missing";
  return (
    <button type="button" className={layout.row} onClick={onOpen}>
      <span className={`${layout.cover} ${missing ? layout.dim : ""}`}>
        {pack.coverUrl && <img src={pack.coverUrl} alt="" />}
      </span>
      <span className={layout.text}>
        <span className={layout.title}>{pack.title}</span>
        <span className={layout.meta}>{countLabel(pack)}</span>
      </span>
      {missing ? (
        <Badge tone="warning" icon="alert">
          Не найден
        </Badge>
      ) : (
        isFull(pack) && (
          <Badge tone="warning" icon="alert">
            Заполнен
          </Badge>
        )
      )}
      <Icon name="chevron-right" size={18} strokeWidth={2} className={layout.chevron} />
    </button>
  );
}
