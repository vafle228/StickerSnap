import type { components } from "./schema";

type Schemas = components["schemas"];

export type Pack = Schemas["Pack"];
export type PackDetail = Schemas["PackDetail"];
export type PackKind = Schemas["PackKind"];
export type Sticker = Schemas["Sticker"];
export type Device = Schemas["Device"];
export type LinkRequest = Schemas["LinkRequest"];
export type FeedbackRequest = Schemas["FeedbackRequest"];
export type FeedbackType = Schemas["FeedbackType"];
export type Problem = Schemas["Problem"];
export type ErrorCode = Schemas["ErrorCode"];
