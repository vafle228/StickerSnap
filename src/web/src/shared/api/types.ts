import type { components } from "./schema";

// Short names for the contract types. They come only from schema.d.ts, generated from
// src/contract/openapi.yaml: a type the screens need but the file lacks is a contract change.
type Schemas = components["schemas"];

export type AppInfo = Schemas["AppInfo"];
export type Settings = Schemas["Settings"];
export type SettingsPatch = Schemas["SettingsPatch"];
export type Theme = Schemas["Theme"];
export type Accent = Schemas["Accent"];
export type SourceRef = Schemas["SourceRef"];
export type Upload = Schemas["Upload"];
export type Project = Schemas["Project"];
export type ProjectSummary = Schemas["ProjectSummary"];
export type Scene = Schemas["Scene"];
export type Segment = Schemas["Segment"];
export type SegmentPreset = Schemas["SegmentPreset"];
export type Crop = Schemas["Crop"];
export type Stretch = Schemas["Stretch"];
export type SourceInfo = Schemas["SourceInfo"];
export type PresetLimits = Schemas["PresetLimits"];
export type ValidationReport = Schemas["ValidationReport"];
export type Estimate = Schemas["Estimate"];
export type Concession = Schemas["Concession"];
export type LadderStep = Schemas["LadderStep"];
export type ExportResult = Schemas["ExportResult"];
export type Job = Schemas["Job"];
export type JobRequest = Schemas["JobRequest"];
export type JobItem = Schemas["JobItem"];
export type AccountLink = Schemas["AccountLink"];
export type PackSummary = Schemas["PackSummary"];
export type Pack = Schemas["Pack"];
export type PackSticker = Schemas["PackSticker"];
export type PackType = Schemas["PackType"];
export type PublishDraft = Schemas["PublishDraft"];
export type PackNameCheck = Schemas["PackNameCheck"];
export type FeedbackType = Schemas["FeedbackType"];
export type FeedbackRequest = Schemas["FeedbackRequest"];
export type ErrorSource = Schemas["ErrorSource"];
export type Problem = Schemas["Problem"];
export type ErrorCode = Schemas["ErrorCode"];
