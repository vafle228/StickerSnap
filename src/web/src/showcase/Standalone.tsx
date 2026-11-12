import { useEffect, useState, type CSSProperties, type ReactNode } from "react";
import { createRoot } from "react-dom/client";
import {
  Button,
  Collapse,
  ICON_NAMES,
  Icon,
  Menu,
  Segmented,
  StickerArt,
  SwatchPicker,
  freezeMotion,
  type AccentName,
} from "@stickersnap/ds";
import "@stickersnap/ds/index.css";
import { Specimens } from "./Specimens";
import "./standalone.css";

type Theme = "light" | "dark";
type Token = {
  name: string;
  value: string | number | Record<Theme, string>;
  usage?: string;
  reduced?: string;
};
type TokenGroup = { note?: string; tokens: Token[] };
type TypeStyle = {
  name: string;
  fontSize: string;
  lineHeight: string;
  fontWeight: number;
  letterSpacing?: string;
  sample: string;
  usage: string;
};
declare const __DESIGN_SYSTEM__: {
  tokens: Record<"spacing" | "radius" | "shadow" | "timing" | "size", TokenGroup> & {
    color: TokenGroup & { themes: { id: Theme; name: string }[] };
    type: {
      families: Record<string, string>;
      groups: { name: string; family: string; styles: TypeStyle[] }[];
    };
  };
  components: { name: string; file: string; description: string; props: string }[];
  licenses: { name: string; file: string; text: string }[];
};

const { tokens, components, licenses } = __DESIGN_SYSTEM__;
const sections = [
  ["components", "Компоненты"],
  ["rules", "Правила"],
  ["colors", "Цвета"],
  ["type", "Типографика"],
  ["dimensions", "Размеры и рельеф"],
  ["motion", "Движение"],
  ["icons", "Иконки"],
  ["api", "API компонентов"],
  ["sources", "Источники"],
] as const;

function Section({ id, title, children }: { id: string; title: string; children: ReactNode }) {
  return (
    <section id={id} className="ds-doc-section">
      <h2>{title}</h2>
      {children}
    </section>
  );
}

const valueOf = (token: Token, theme: Theme) =>
  typeof token.value === "object" ? token.value[theme] : String(token.value);

function TokenTable({
  group,
  preview,
}: {
  group: TokenGroup;
  preview?: "color" | "shadow" | "radius" | "size";
}) {
  return (
    <>
      {group.note && <p className="ds-doc-muted">{group.note}</p>}
      <div className="ds-doc-table-wrap">
        <table>
          <thead>
            <tr>
              <th>CSS-переменная</th>
              <th>Светлая тема</th>
              <th>Тёмная тема</th>
              <th>Применение</th>
            </tr>
          </thead>
          <tbody>
            {group.tokens.map((token) => (
              <tr key={token.name}>
                <th scope="row">
                  <code>--{token.name}</code>
                </th>
                {tokens.color.themes.map(({ id }) => (
                  <td key={id}>
                    <div className="ds-doc-token" data-theme={id}>
                      {preview && (
                        <span
                          className={`ds-doc-sample ds-doc-sample-${preview}`}
                          style={{
                            ...(preview === "color" && { background: `var(--${token.name})` }),
                            ...(preview === "shadow" && { boxShadow: `var(--${token.name})` }),
                            ...(preview === "radius" && { borderRadius: `var(--${token.name})` }),
                            ...(preview === "size" && { width: `var(--${token.name})` }),
                          }}
                        />
                      )}
                      <code>{valueOf(token, id)}</code>
                    </div>
                  </td>
                ))}
                <td>
                  {token.usage}
                  {token.reduced && (
                    <p>
                      <strong>Меньше движения:</strong> <code>{token.reduced}</code>
                    </p>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}

function Extras() {
  const [open, setOpen] = useState(true);
  const [choice, setChoice] = useState("first");
  return (
    <div className="ds-doc-extras">
      <h3>Collapse, StickerArt и управляемое Menu</h3>
      <Button aria-expanded={open} onClick={() => setOpen(!open)}>
        {open ? "Свернуть пример" : "Развернуть пример"}
      </Button>
      <Collapse open={open}>
        <div className="ds-doc-collapse">
          <div className="ds-doc-art">
            <StickerArt />
          </div>
          <p>
            Collapse плавно освобождает место после закрытия. StickerArt показывает заглушку, когда
            превью ещё нет.
          </p>
        </div>
      </Collapse>
      <Menu
        label="Демонстрация выбора"
        selectable
        value={choice}
        onSelect={setChoice}
        items={[
          { value: "first", label: "Первый вариант", icon: "film" },
          { value: "second", label: "Второй вариант", icon: "folder" },
          { value: "disabled", label: "Недоступный вариант", disabled: true },
        ]}
      />
      <p role="status" className="ds-doc-muted">
        Выбрано: {choice === "first" ? "первый вариант" : "второй вариант"}.
      </p>
    </div>
  );
}

export function Standalone() {
  const [accent, setAccent] = useState<AccentName>("indigo");
  const [theme, setTheme] = useState<Theme>("light");
  const [filter, setFilter] = useState("");
  const visible = components.filter((component) =>
    component.name.toLowerCase().includes(filter.toLowerCase()),
  );

  useEffect(() => {
    const root = document.documentElement;
    root.dataset.theme = theme;
    root.className = `ss-accent-${accent}`;
    freezeMotion(root);
  }, [accent, theme]);

  return (
    <div className="ss-root ds-doc">
      <header className="ds-doc-header">
        <div>
          <p className="ds-doc-kicker">STICKERSNAP · @stickersnap/ds</p>
          <h1>Мягкий и плотный</h1>
          <p className="ds-doc-lead">
            Дизайн-система в действии: {components.length} компонента, две темы, пять акцентов.
            Точные токены и интерфейсы из исходного кода.
          </p>
        </div>
        <div className="ds-doc-controls">
          <SwatchPicker value={accent} onChange={setAccent} />
          <Segmented
            label="Тема документа и всплывающих окон"
            value={theme}
            onChange={setTheme}
            items={[
              { value: "light", label: "Светлая" },
              { value: "dark", label: "Тёмная" },
            ]}
          />
        </div>
      </header>
      <nav className="ds-doc-nav" aria-label="Оглавление">
        {sections.map(([id, label]) => (
          <a key={id} href={`#${id}`}>
            {label}
          </a>
        ))}
      </nav>
      <main>
        <Section id="components" title="Компоненты · живые примеры">
          <p>
            Нажимайте кнопки, меняйте выбор, открывайте списки и окна. Обе колонки используют
            компоненты приложения. Акцент общий; модальные окна берут тему документа, выбранную
            сверху.
          </p>
          <p className="ds-doc-muted">
            Это локальные примеры: импорт, публикация, удаление и другие операции приложения здесь
            не выполняются. LinkChip показывает пример ссылки Telegram для копирования.
          </p>
          <div className="ds-doc-columns">
            {tokens.color.themes.map(({ id, name }) => (
              <div key={id} data-theme={id} className={`ss-root ds-doc-column ss-accent-${accent}`}>
                <h3 className="ds-doc-theme">{name}</h3>
                <div className="ds-doc-specimens">
                  <Specimens id={`standalone-${id}`} />
                </div>
                <Extras />
              </div>
            ))}
          </div>
        </Section>
        <Section id="rules" title="Как применять">
          <div className="ds-doc-rules">
            <article>
              <h3>Общие основы</h3>
              <p>
                Импортируйте компоненты из <code>@stickersnap/ds</code> и один раз подключайте{" "}
                <code>@stickersnap/ds/index.css</code>. Экраны добавляют CSS Modules. Цвета, отступы
                и движение берите из семантических CSS-переменных.
              </p>
              <p>
                Тема задаётся <code>data-theme="light|dark"</code>, акцент — классом{" "}
                <code>ss-accent-*</code>. Manrope — основной шрифт, JetBrains Mono — тайм-коды и
                числовые данные.
              </p>
            </article>
            <article>
              <h3>Действия и выбор</h3>
              <p>
                <code>primary</code> — основное действие, <code>secondary</code> — обычное,{" "}
                <code>ghost</code> — тихое, <code>danger</code> — необратимое. <code>prism</code> —
                специальное действие публикации, одно на экран; акцент не меняет радугу.
              </p>
              <p>
                <code>Select</code> — пять и более вариантов; для двух–четырёх —{" "}
                <code>Segmented</code> или <code>RadioGroup</code>. Disabled-кнопке при
                необходимости передавайте <code>reason</code>. У <code>IconButton</code> обязательна
                текстовая <code>label</code>.
              </p>
            </article>
            <article>
              <h3>Окна и фокус</h3>
              <p>
                Слои: popover → dialog → confirm. Esc закрывает только верхний; новое окно закрывает
                открытые popover. Диалог удерживает Tab и возвращает фокус инициатору. Клик на
                затемнение закрывает только окно с <code>dismissible</code>.
              </p>
              <p>
                <code>ConfirmDialog</code> — для необратимых действий: вопрос и кнопка называют
                действие, начальный фокус на «Отмена». Удаление с возможностью отмены не требует
                такого подтверждения.
              </p>
            </article>
            <article>
              <h3>Обратная связь и движение</h3>
              <p>
                <code>Banner</code> объясняет состояние рядом с контентом; <code>Toast</code>{" "}
                сообщает исход или прогресс. <code>ToastStack</code> вытесняет старые info/success
                при превышении трёх; предупреждения, ошибки и активные задачи сохраняются, поэтому
                стек может быть больше.
              </p>
              <p>
                Используйте <code>usePresence</code>/<code>Collapse</code> для выхода. Фокус,
                выделение пунктов, ползунок, playhead и перетаскивание не анимируются. При смене
                темы вызывайте <code>freezeMotion</code>; системное уменьшение движения учитывается
                токенами и CSS.
              </p>
            </article>
          </div>
        </Section>
        <Section id="colors" title={`Цвета · ${tokens.color.tokens.length} токенов`}>
          <p>
            Значения и назначения приведены напрямую из <code>src/ds/src/tokens.json</code>. Ссылки{" "}
            <code>{"{token}"}</code> превращаются генератором в <code>var(--token)</code>. Образцы
            показывают исходные значения соответствующей темы.
          </p>
          <TokenTable group={tokens.color} preview="color" />
        </Section>
        <Section id="type" title="Типографика">
          {Object.entries(tokens.type.families).map(([name, family]) => (
            <p key={name}>
              <code>--font-{name}</code>: {family}
            </p>
          ))}
          <div className="ds-doc-type">
            {tokens.type.groups.flatMap((group) =>
              group.styles.map((style) => (
                <article key={style.name}>
                  <p
                    className="ds-doc-type-sample"
                    style={
                      {
                        font: `var(--type-${style.name})`,
                        letterSpacing: style.letterSpacing,
                      } as CSSProperties
                    }
                  >
                    {style.sample}
                  </p>
                  <code>
                    --type-{style.name} · {style.fontWeight} · {style.fontSize}/{style.lineHeight}
                    {style.letterSpacing ? ` · ${style.letterSpacing}` : ""}
                  </code>
                  <p>{style.usage}</p>
                </article>
              )),
            )}
          </div>
        </Section>
        <Section id="dimensions" title="Размеры и рельеф">
          <h3>Отступы</h3>
          <TokenTable group={tokens.spacing} preview="size" />
          <h3>Размеры элементов</h3>
          <TokenTable group={tokens.size} preview="size" />
          <h3>Скругления</h3>
          <TokenTable group={tokens.radius} preview="radius" />
          <h3>Тени</h3>
          <TokenTable group={tokens.shadow} preview="shadow" />
        </Section>
        <Section id="motion" title="Движение">
          <TokenTable group={tokens.timing} />
          <p>
            Значения <code>reduced</code> применяются при{" "}
            <code>prefers-reduced-motion: reduce</code>. Фактические переходы компонентов определены
            в <code>src/ds/src/motion.css</code>; поведение сворачивания и окон можно проверить в
            примерах выше.
          </p>
        </Section>
        <Section id="icons" title={`Иконки · ${ICON_NAMES.length}`}>
          <p>
            Векторные иконки из <code>src/ds/src/icons/paths.ts</code>: сетка 24 px, обводка 1,75
            px, круглые окончания. Используйте <code>{'<Icon name="film" size={20} />'}</code>.
          </p>
          <div className="ds-doc-icons">
            {ICON_NAMES.map((name) => (
              <div key={name}>
                <Icon name={name} size={24} />
                <code>{name}</code>
              </div>
            ))}
          </div>
        </Section>
        <Section id="api" title={`API · ${components.length} экспортируемых компонентов`}>
          <p>
            Интерфейсы и комментарии извлечены из файлов компонентов при сборке. Общие типы и hooks
            доступны в <code>src/ds/src/types.ts</code>, <code>hooks.ts</code> и{" "}
            <code>layers.ts</code>.
          </p>
          <label className="ds-doc-filter">
            Найти компонент
            <input
              value={filter}
              onChange={(event) => setFilter(event.target.value)}
              type="search"
              placeholder="Например, Dialog"
            />
          </label>
          <p className="ds-doc-muted" role="status">
            Показано {visible.length} из {components.length}
          </p>
          <div className="ds-doc-api">
            {visible.map((component) => (
              <details key={component.name}>
                <summary>
                  <strong>{component.name}</strong>
                  <code>{component.file}</code>
                </summary>
                {component.description && <p>{component.description}</p>}
                {component.props ? (
                  <pre>
                    <code>{component.props}</code>
                  </pre>
                ) : (
                  <p>Публичные props не объявлены.</p>
                )}
              </details>
            ))}
          </div>
        </Section>
        <Section id="sources" title="Источники и воспроизведение">
          <p>
            Этот файл собран из <code>src/ds</code> и{" "}
            <code>src/web/src/showcase/Specimens.tsx</code>. Компоненты, CSS, SVG и WOFF2 встроены в
            HTML: он работает без сервера и сети. Каталог обновляется из исходников, вручную HTML не
            редактируется.
          </p>
          <pre>
            <code>
              {
                "npm ci\nnpm run gen:tokens\nnpm run build:design-system\nnpm run build:design-system -- --check"
              }
            </code>
          </pre>
          <p>
            Источник токенов: <code>src/ds/src/tokens.json</code>. Генератор CSS:{" "}
            <code>tools/build-tokens.mjs</code>. Генератор этой страницы:{" "}
            <code>tools/build-design-system.mjs</code>. Живая dev-витрина:{" "}
            <code>http://127.0.0.1:5173/?showcase</code> после <code>npm run dev</code>.
          </p>
          <h3>Лицензии встроенных ресурсов</h3>
          {licenses.map((license) => (
            <details key={license.name}>
              <summary>
                {license.name} · <code>{license.file}</code>
              </summary>
              <pre>
                <code>{license.text}</code>
              </pre>
            </details>
          ))}
        </Section>
      </main>
      <footer className="ds-doc-footer">
        StickerSnap · локальная документация из исходного кода
      </footer>
    </div>
  );
}

const root = document.getElementById("root");
if (!root) throw new Error("The standalone document needs #root.");
createRoot(root).render(<Standalone />);
