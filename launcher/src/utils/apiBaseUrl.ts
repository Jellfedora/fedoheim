import { invoke } from "@tauri-apps/api/core";

// Mémoïsé : la valeur ne change pas pendant la durée de vie du process.
let cached: Promise<string> | null = null;

export function getApiBaseUrl(): Promise<string> {
  if (!cached) {
    cached = invoke<string>("api_base_url");
  }
  return cached;
}
