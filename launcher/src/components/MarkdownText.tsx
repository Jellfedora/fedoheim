import { Fragment, useState } from "react";
import "./MarkdownText.css";

// Sous-ensemble du markdown Discord (mêmes marqueurs, pour pouvoir un jour reposter
// ce texte tel quel dans un salon Discord) : **gras**, *italique*, __souligné__,
// ~~barré~~, ||spoiler||. Pas de gestion des combinaisons imbriquées (ex: gras+italique).
const INLINE_TOKEN_PATTERN =
  /(\|\|[\s\S]+?\|\||\*\*[\s\S]+?\*\*|__[\s\S]+?__|~~[\s\S]+?~~|\*[\s\S]+?\*|_[\s\S]+?_)/g;

// Marqueurs de bloc Discord réellement utilisés par les admins (voir l'annonce type
// qui a motivé cet ajout : titres, citation, liste, séparateur) — jamais parsés avant,
// juste affichés tels quels ("# Titre", "> citation"...) alors que Discord les rend
// correctement, d'où l'écart de rendu entre le launcher et Discord.
const HEADING_PATTERN = /^(#{1,3})\s+(.*)$/;
const QUOTE_PATTERN = /^>\s?(.*)$/;
const LIST_ITEM_PATTERN = /^[-*]\s+(.*)$/;
const HR_PATTERN = /^(-{3,}|\*{3,}|_{3,})$/;

function Spoiler({ children }: { children: React.ReactNode }) {
  const [revealed, setRevealed] = useState(false);
  return (
    <span
      className={`markdown-spoiler ${revealed ? "is-revealed" : ""}`}
      onClick={() => setRevealed(true)}
    >
      {children}
    </span>
  );
}

// Formatage en ligne (une seule ligne de texte) — extrait de MarkdownText pour être
// réutilisé par chaque type de bloc (paragraphe, titre, item de liste, citation).
function renderInline(text: string, keyPrefix: string): React.ReactNode[] {
  const nodes: React.ReactNode[] = [];
  let lastIndex = 0;
  let key = 0;
  INLINE_TOKEN_PATTERN.lastIndex = 0;

  let match: RegExpExecArray | null;
  while ((match = INLINE_TOKEN_PATTERN.exec(text))) {
    if (match.index > lastIndex) {
      nodes.push(
        <Fragment key={`${keyPrefix}-${key++}`}>{text.slice(lastIndex, match.index)}</Fragment>,
      );
    }

    const raw = match[0];
    const k = `${keyPrefix}-${key++}`;
    if (raw.startsWith("||")) {
      nodes.push(<Spoiler key={k}>{raw.slice(2, -2)}</Spoiler>);
    } else if (raw.startsWith("**")) {
      nodes.push(<strong key={k}>{raw.slice(2, -2)}</strong>);
    } else if (raw.startsWith("__")) {
      nodes.push(<u key={k}>{raw.slice(2, -2)}</u>);
    } else if (raw.startsWith("~~")) {
      nodes.push(<s key={k}>{raw.slice(2, -2)}</s>);
    } else {
      nodes.push(<em key={k}>{raw.slice(1, -1)}</em>);
    }

    lastIndex = INLINE_TOKEN_PATTERN.lastIndex;
  }

  if (lastIndex < text.length) {
    nodes.push(<Fragment key={`${keyPrefix}-${key++}`}>{text.slice(lastIndex)}</Fragment>);
  }

  return nodes;
}

function linesWithBreaks(lines: string[], keyPrefix: string) {
  return lines.map((line, i) => (
    <Fragment key={i}>
      {renderInline(line, `${keyPrefix}-${i}`)}
      {i < lines.length - 1 && <br />}
    </Fragment>
  ));
}

// Rendu bloc par bloc du sous-ensemble Discord ci-dessus, ligne par ligne — pas un
// vrai parseur markdown (pas d'imbrication de blocs, une liste ne peut pas contenir de
// citation, etc.), volontairement limité à ce qu'un admin écrit réellement dans une
// annonce. Le formatage en ligne (renderInline) s'applique dans chaque type de bloc.
export function MarkdownText({ text }: { text: string }) {
  const lines = text.split("\n");
  const blocks: React.ReactNode[] = [];
  let paragraphLines: string[] = [];
  let listItems: string[] = [];
  let quoteLines: string[] = [];
  let key = 0;

  function flushParagraph() {
    if (paragraphLines.length === 0) return;
    blocks.push(
      <p key={`p${key++}`} className="markdown-paragraph">
        {linesWithBreaks(paragraphLines, `p${key}`)}
      </p>,
    );
    paragraphLines = [];
  }

  function flushList() {
    if (listItems.length === 0) return;
    const k = key++;
    blocks.push(
      <ul key={`ul${k}`} className="markdown-list">
        {listItems.map((item, i) => (
          <li key={i}>{renderInline(item, `ul${k}-${i}`)}</li>
        ))}
      </ul>,
    );
    listItems = [];
  }

  function flushQuote() {
    if (quoteLines.length === 0) return;
    blocks.push(
      <blockquote key={`q${key++}`} className="markdown-quote">
        {linesWithBreaks(quoteLines, `q${key}`)}
      </blockquote>,
    );
    quoteLines = [];
  }

  function flushAll() {
    flushParagraph();
    flushList();
    flushQuote();
  }

  for (const line of lines) {
    if (line.trim() === "") {
      flushAll();
      continue;
    }

    if (HR_PATTERN.test(line.trim())) {
      flushAll();
      blocks.push(<hr key={`hr${key++}`} className="markdown-hr" />);
      continue;
    }

    const heading = HEADING_PATTERN.exec(line);
    if (heading) {
      flushAll();
      const level = heading[1].length;
      blocks.push(
        <div key={`h${key}`} className={`markdown-heading markdown-heading--${level}`}>
          {renderInline(heading[2], `h${key++}`)}
        </div>,
      );
      continue;
    }

    const quote = QUOTE_PATTERN.exec(line);
    if (quote) {
      flushParagraph();
      flushList();
      quoteLines.push(quote[1]);
      continue;
    }

    const listItem = LIST_ITEM_PATTERN.exec(line);
    if (listItem) {
      flushParagraph();
      flushQuote();
      listItems.push(listItem[1]);
      continue;
    }

    flushList();
    flushQuote();
    paragraphLines.push(line);
  }
  flushAll();

  return <>{blocks}</>;
}
