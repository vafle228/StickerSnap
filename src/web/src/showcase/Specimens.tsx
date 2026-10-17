import { useState, type ReactNode } from "react";
import {
  Badge,
  Banner,
  Button,
  Card,
  Checkbox,
  ConfirmDialog,
  Dialog,
  DropZone,
  EmojiChips,
  ICON_NAMES,
  Icon,
  IconButton,
  Kbd,
  LinkChip,
  Menu,
  NavRail,
  PackRow,
  Progress,
  ProjectCard,
  RadioGroup,
  SaveStatus,
  SectionNav,
  Segmented,
  Select,
  Slider,
  StickerTile,
  Switch,
  Tabs,
  TextField,
  Timeline,
  Toast,
  ToastStack,
} from "@stickersnap/ds";
import styles from "./Showcase.module.css";

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className={styles.section}>
      <h2 className={styles.eyebrow}>{title}</h2>
      {children}
    </section>
  );
}

function Row({ children }: { children: ReactNode }) {
  return <div className={styles.row}>{children}</div>;
}

/** Every component in its states. Rendered once per theme column. */
export function Specimens({ id }: { id: string }) {
  const [emoji, setEmoji] = useState(["😹", "🛋", "🙀"]);
  const [dialog, setDialog] = useState(false);
  const [confirm, setConfirm] = useState(false);

  return (
    <div className={styles.specimens}>
      <Section title="Иконки">
        <div className={styles.icons}>
          {ICON_NAMES.map((name) => (
            <span key={name} className={styles.icon} title={name}>
              <Icon name={name} size={20} />
            </span>
          ))}
        </div>
      </Section>

      <Section title="Кнопки">
        <Row>
          <Button variant="primary" icon="upload">
            Импорт видео
          </Button>
          <Button>Экспорт WEBM</Button>
          <Button variant="ghost">Пропустить</Button>
          <Button variant="danger">Удалить</Button>
        </Row>
        <Row>
          <Button variant="prism" icon="send">
            Опубликовать в Telegram
          </Button>
          <Button variant="prism" icon="send" disabled reason="Нужен исходник">
            Опубликовать в Telegram
          </Button>
        </Row>
        <Row>
          <Button size="sm" variant="primary">
            Вернуть
          </Button>
          <Button size="sm">Подробности</Button>
          <IconButton icon="scissors" label="Обрезать" active />
          <IconButton icon="crop" label="Кадрировать" />
          <IconButton icon="bell" label="Новое" dot />
          <IconButton icon="more" label="Ещё" flat size="sm" />
          <Kbd keys={["Ctrl", "Shift", "Z"]} />
          <Kbd>F2</Kbd>
        </Row>
      </Section>

      <Section title="Поля и подсказки у поля">
        <div className={styles.grid2}>
          <TextField label="Название пака" placeholder="Кот и диван" hint="До 64 символов" />
          <TextField icon="search" placeholder="Поиск по проектам" aria-label="Поиск" />
          <TextField
            label="Ссылка"
            defaultValue="kot_divan"
            suffix="_by_StickerSnapAppBot"
            error="Ссылка уже занята"
            fix={{ label: "Взять kot_divan_2", onClick: () => undefined }}
          />
          <TextField
            label="Длина"
            defaultValue="3,4 с"
            warning="Telegram примет до 3 с: лишнее обрежем"
          />
        </div>
      </Section>

      <Section title="Выбор">
        <Row>
          <Switch label="Точное превью" defaultChecked />
          <Switch label="Звук" />
          <Checkbox label="Отрезок 1" defaultChecked />
          <Checkbox label="Отрезок 2" />
          <Checkbox label="Недоступно" disabled />
        </Row>
        <Row>
          <Segmented label="Тема" items={["Светлая", "Тёмная", "Как в системе"]} />
          <Select
            label="Проект"
            placeholder="Выберите проект"
            items={["Кот на клавиатуре", "Танец под припев", "Лето 2026", "Первый тест", "Реакции"]}
          />
        </Row>
        <RadioGroup
          label="Куда публиковать"
          columns={2}
          items={[
            { value: "mine", label: "Мой пак", description: "Добавить в пак, который уже есть" },
            { value: "new", label: "Новый пак", description: "Создать пак с этими стикерами" },
          ]}
        />
        <div className={styles.grid2}>
          <Slider
            label="Скорость"
            min={0.25}
            max={2}
            step={0.25}
            defaultValue={1.25}
            format={(v) => `${String(v).replace(".", ",")}×`}
          />
          <Slider
            label="Кадров в секунду"
            min={10}
            max={30}
            defaultValue={24}
            format={(v) => `${v} fps`}
          />
        </div>
        <EmojiChips
          value={emoji}
          onRemove={(i) => setEmoji(emoji.filter((_, j) => j !== i))}
          onAdd={() => setEmoji([...emoji, "🔥"])}
        />
      </Section>

      <Section title="Навигация">
        <Tabs items={["Экспорт", "Кадры", "Звук"]} />
        <Row>
          <NavRail
            narrow
            height={260}
            items={[
              { value: "editor", label: "Редактор", icon: "film" },
              { value: "projects", label: "Проекты", icon: "folder" },
              { value: "packs", label: "Мои паки", icon: "sticker", dot: true },
            ]}
            bottomItems={[{ value: "settings", label: "Настройки", icon: "settings" }]}
          />
          <NavRail
            height={260}
            items={[
              { value: "editor", label: "Редактор", icon: "film" },
              { value: "projects", label: "Проекты", icon: "folder" },
              { value: "packs", label: "Мои паки", icon: "sticker", count: 2 },
            ]}
            bottomItems={[{ value: "settings", label: "Настройки", icon: "settings" }]}
          />
          <SectionNav
            items={[
              { value: `${id}-look`, label: "Оформление" },
              { value: `${id}-telegram`, label: "Telegram" },
              { value: `${id}-about`, label: "О программе" },
            ]}
          />
        </Row>
        <Menu
          label="Стикер"
          items={[
            { value: "open", label: "Открыть проект", icon: "folder" },
            { value: "rename", label: "Переименовать", icon: "text", shortcut: "F2" },
            { value: "copy", label: "Дублировать", icon: "copy", shortcut: ["Ctrl", "D"] },
            "-",
            { value: "delete", label: "Удалить из пака…", icon: "trash", danger: true },
          ]}
        />
      </Section>

      <Section title="Карточки">
        <Card eyebrow="Экспорт">
          <div className="ss-row">512 × 512</div>
          <div className="ss-row">184 КБ</div>
          <div className="ss-row">00:02.840</div>
        </Card>
        <Card variant="sunken" eyebrow="Кадры">
          <Timeline />
        </Card>
      </Section>

      <Section title="Оповещения · метки">
        <Row>
          <Badge tone="info" icon="info">
            Инфо
          </Badge>
          <Badge tone="success" icon="check-circle">
            Готово
          </Badge>
          <Badge tone="warning" icon="alert">
            Предупреждение
          </Badge>
          <Badge tone="danger" icon="error-circle">
            Ошибка
          </Badge>
          <Badge tone="info" busy>
            отправляем…
          </Badge>
          <Badge>WEBM</Badge>
          <Badge tone="primary">512 × 512</Badge>
          <Badge tone="highlight">Новое</Badge>
        </Row>
        <Row>
          <SaveStatus state="saved" />
          <SaveStatus state="saving" />
          <SaveStatus state="error" action={<Button size="sm">Сохранить в другое место…</Button>} />
          <LinkChip href="https://t.me/addstickers/kot_divan_by_StickerSnapAppBot" />
        </Row>
      </Section>

      <Section title="Оповещения · плашки">
        <Banner
          title="Нет видео dog_ball.mp4"
          description="Отрезки и настройки целы. Укажите, куда переместили файл."
          action={<Button size="sm">Указать файл…</Button>}
        />
        <Banner
          tone="info"
          title="Telegram просит подождать"
          description="Продолжим отправку через 30 с."
          onClose={() => undefined}
        />
        <Banner tone="danger" title="Пак не найден" description="Его удалили в Telegram." />
      </Section>

      <Section title="Оповещения · поп-апы">
        <ToastStack inline>
          <Toast
            title="Проект убран из списка"
            description="«Танец под припев» остался на диске."
            action={<Button size="sm">Вернуть</Button>}
            onClose={() => undefined}
            timeout={null}
          />
          <Toast
            tone="success"
            title="3 стикера в паке «Кот и диван»"
            description="В Telegram появятся через пару минут."
            action={<Button size="sm">Открыть в Telegram</Button>}
            onClose={() => undefined}
            timeout={null}
          />
          <Toast
            tone="danger"
            title="Не получилось открыть clip.mp4"
            description="Файл повреждён: в нём нет ни одного целого кадра."
            action={<Button size="sm">Выбрать другой</Button>}
            secondaryAction={
              <Button size="sm" variant="ghost">
                Подробности
              </Button>
            }
            onClose={() => undefined}
          />
          <Toast
            title="Отправляем 2 из 3"
            description="в «Кот и диван»"
            progress={62}
            progressLabel="Отправляем в Telegram"
            progressVariant="rainbow"
            action={<Button size="sm">Открыть</Button>}
            onClose={() => undefined}
          />
        </ToastStack>
        <Toast
          tone="warning"
          title="Отрезок 3 выходит за конец видео"
          description="Новое видео короче прежнего."
          action={<Button size="sm">Показать</Button>}
          onClose={() => undefined}
        />
        <Progress value={42} label="Кодирую в VP9" />
        <Progress value={62} label="Отправляем в Telegram" variant="rainbow" />
      </Section>

      <Section title="Окна">
        <Row>
          <Button onClick={() => setDialog(true)}>Открыть окно</Button>
          <Button onClick={() => setConfirm(true)}>Открыть подтверждение</Button>
        </Row>
        <div className={styles.stage}>
          <Dialog
            inline
            title="Добавить по ссылке"
            text="Вставьте ссылку на пак, созданный нашим ботом."
            onClose={() => undefined}
            actions={
              <>
                <Button>Отмена</Button>
                <Button variant="primary">Добавить</Button>
              </>
            }
          >
            <TextField aria-label="Ссылка" placeholder="t.me/addstickers/…" />
          </Dialog>
        </div>
        <div className={styles.stage}>
          <ConfirmDialog
            inline
            title="Удалить стикер из пака?"
            text="Он пропадёт у всех, кто добавил пак."
            confirmLabel="Удалить"
            onConfirm={() => undefined}
            onCancel={() => undefined}
          />
        </div>
        <Dialog
          open={dialog}
          wide
          title="Опубликовать в Telegram"
          text="Esc закрывает верхний слой: сначала список, потом окно. Клик мимо окно не закрывает."
          onClose={() => setDialog(false)}
          actions={
            <>
              <Button onClick={() => setDialog(false)}>Отмена</Button>
              <Button variant="prism" icon="send" onClick={() => setConfirm(true)}>
                Опубликовать 3
              </Button>
            </>
          }
        >
          <Select label="Проект" items={["Кот на клавиатуре", "Танец под припев", "Лето 2026"]} />
        </Dialog>
        <ConfirmDialog
          open={confirm}
          title="Удалить стикер из пака?"
          text="Он пропадёт у всех, кто добавил пак."
          confirmLabel="Удалить"
          onConfirm={() => setConfirm(false)}
          onCancel={() => setConfirm(false)}
        />
      </Section>

      <Section title="Контент">
        <DropZone />
        <Row>
          <DropZone variant="tile" title="Новый проект" hint={null} className={styles.tile} />
          <DropZone dragging className={styles.tile} />
        </Row>
        <Row>
          <StickerTile name="Стикер 1" duration="2,4 с" emoji={["😹", "🛋"]} />
          <StickerTile
            name="Стикер 2"
            duration="1,8 с"
            selected
            emoji={["😴", "👍", "🔥", "🙈", "🤔"]}
            project="Кот на клавиатуре"
            badges={[<Badge key="f">WEBM</Badge>]}
          />
        </Row>
        <Row>
          <ProjectCard
            name="Кот на клавиатуре"
            meta="3 стикера · вчера"
            duration="0:12"
            onMenu={() => undefined}
          />
          <ProjectCard name="Мяч" meta="1 стикер · 2 окт." missing onMenu={() => undefined} />
        </Row>
        <Card>
          <div role="list">
            <PackRow
              title="Кот и диван"
              count={42}
              onMenu={() => undefined}
              badges={
                <Badge tone="info" busy>
                  отправляем…
                </Badge>
              }
            />
            <PackRow title="Реакции с созвона" count={120} onMenu={() => undefined} />
            <PackRow title="Первый тест" missing onMenu={() => undefined} />
          </div>
        </Card>
      </Section>

      <Section title="Секции для SectionNav">
        <p id={`${id}-look`} className={styles.note}>
          Оформление
        </p>
        <p id={`${id}-telegram`} className={styles.note}>
          Telegram
        </p>
        <p id={`${id}-about`} className={styles.note}>
          О программе
        </p>
      </Section>
    </div>
  );
}
