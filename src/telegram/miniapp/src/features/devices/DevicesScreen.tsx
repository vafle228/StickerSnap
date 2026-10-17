import { useState } from "react";
import { Button, ConfirmDialog, Icon } from "@stickersnap/ds";
import { isUnauthorized } from "../../api/client";
import { useDevices, useUnlinkDevice } from "../../api/queries";
import type { Device } from "../../api/types";
import { DOWNLOAD_URL } from "../../app/links";
import { Empty, Notice, Page, PageHead, SkeletonRows } from "../../shared/Layout";
import layout from "../../shared/layout.module.css";
import { useToast } from "../../shared/toast";
import { useDelayed } from "../../shared/useDelayed";
import { useTelegram } from "../../telegram/context";
import { deviceLabel } from "./format";
import styles from "./devices.module.css";

/** «Устройства» (artboard 13, section 5): the computers where StickerSnap is linked to this
 *  Telegram account. Linking happens only on the computer, so there is no «Привязать» here. */
export function DevicesScreen() {
  const telegram = useTelegram();
  const devices = useDevices();
  const unlink = useUnlinkDevice();
  const show = useToast((s) => s.show);
  const [asking, setAsking] = useState<Device | null>(null);
  // The window keeps its name while it fades out
  const [named, setNamed] = useState<Device | null>(null);
  const slow = useDelayed(devices.isPending);

  const ask = (device: Device) => {
    telegram.haptic("confirm");
    setAsking(device);
    setNamed(device);
  };
  const confirm = (device: Device) => {
    setAsking(null);
    unlink.mutate(device.id, {
      onSuccess: () => telegram.haptic("done"),
      onError: (error) => {
        if (isUnauthorized(error)) return;
        telegram.haptic("error");
        show({
          tone: "danger",
          title: "Компьютер не отвязан",
          description: "Нет связи с сервером StickerSnap.",
          action: { label: "Повторить", run: () => confirm(device) },
        });
      },
    });
  };

  return (
    <Page label="Устройства">
      <PageHead title="Устройства" sub="Компьютеры, с которых StickerSnap публикует в ваши паки" />
      {devices.isError && (
        <Notice
          tone="danger"
          title="Не удалось загрузить компьютеры"
          text="Нет связи с сервером StickerSnap."
          action={
            <Button size="sm" onClick={() => void devices.refetch()}>
              Повторить
            </Button>
          }
        />
      )}
      {devices.isSuccess && devices.data.length === 0 && (
        <Empty
          art={
            <span className={layout.emptyIcon}>
              <Icon name="computer" size={30} />
            </span>
          }
          title="Компьютеров пока нет"
          text="Привяжите StickerSnap на компьютере: «Настройки» → «Публикация в Telegram». Бот спросит здесь, привязать ли его."
          action={
            <Button icon="download" onClick={() => telegram.openLink(DOWNLOAD_URL)}>
              Скачать StickerSnap
            </Button>
          }
        />
      )}
      {devices.isSuccess && devices.data.length > 0 && (
        <>
          <ul className={layout.list} aria-label="Компьютеры">
            {devices.data.map((device) => {
              const leaving = unlink.isPending && unlink.variables === device.id;
              return (
                <li key={device.id} className={layout.row}>
                  <span className={layout.deviceIcon}>
                    <Icon name="computer" size={22} />
                  </span>
                  <span className={layout.text}>
                    <span className={layout.title}>{device.name}</span>
                    <span className={layout.meta}>{deviceLabel(device)}</span>
                  </span>
                  <Button
                    variant="ghost"
                    size="sm"
                    className={styles.unlink}
                    aria-label={`Отвязать ${device.name}`}
                    disabled={leaving}
                    onClick={() => ask(device)}
                  >
                    Отвязать
                  </Button>
                </li>
              );
            })}
          </ul>
          <p className={layout.foot}>
            Новый компьютер привязывается только в StickerSnap на нём: «Настройки» → «Публикация в
            Telegram».
          </p>
        </>
      )}
      {(devices.isPending ? slow : devices.isError) && (
        <SkeletonRows count={2} faded={devices.isError} />
      )}
      <ConfirmDialog
        open={asking !== null}
        title={`Отвязать ${named?.name ?? ""}?`}
        text="Он больше не сможет публиковать в ваши паки. Сами паки останутся на месте."
        confirmLabel="Отвязать"
        onConfirm={() => asking && confirm(asking)}
        onCancel={() => setAsking(null)}
      />
    </Page>
  );
}
