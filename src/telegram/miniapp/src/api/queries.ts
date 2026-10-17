import { createContext, useContext } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { call, type Api } from "./client";
import type { PackDetail } from "./types";

export const ApiContext = createContext<Api | null>(null);

export function useApi(): Api {
  const api = useContext(ApiContext);
  if (!api) throw new Error("useApi outside <Providers>");
  return api;
}

export const keys = {
  packs: ["packs"] as const,
  pack: (name: string) => ["pack", name] as const,
  devices: ["devices"] as const,
};

export function usePacks() {
  const api = useApi();
  return useQuery({
    queryKey: keys.packs,
    queryFn: async () => (await call(api.GET("/v1/miniapp/packs"))).packs,
  });
}

export function usePack(name: string) {
  const api = useApi();
  return useQuery({
    queryKey: keys.pack(name),
    queryFn: () => call(api.GET("/v1/miniapp/packs/{name}", { params: { path: { name } } })),
  });
}

export function useDevices() {
  const api = useApi();
  return useQuery({
    queryKey: keys.devices,
    queryFn: async () => (await call(api.GET("/v1/miniapp/devices"))).devices,
  });
}

export function useDeleteSticker(name: string) {
  const api = useApi();
  const client = useQueryClient();
  return useMutation({
    mutationFn: (stickerId: string) =>
      call(
        api.DELETE("/v1/miniapp/packs/{name}/stickers/{stickerId}", {
          params: { path: { name, stickerId } },
        }),
      ),
    onSuccess: (_, stickerId) => {
      // The sticker leaves the grid at once; the last one takes the pack with it (contract)
      const pack = client.getQueryData<PackDetail>(keys.pack(name));
      const stickers = pack?.stickers.filter((s) => s.id !== stickerId) ?? [];
      if (pack && stickers.length > 0) {
        client.setQueryData<PackDetail>(keys.pack(name), {
          ...pack,
          stickers,
          count: stickers.length,
        });
      } else {
        client.removeQueries({ queryKey: keys.pack(name) });
      }
      return client.invalidateQueries({ queryKey: keys.packs });
    },
  });
}

export function useForgetPack() {
  const api = useApi();
  const client = useQueryClient();
  return useMutation({
    mutationFn: (name: string) =>
      call(api.DELETE("/v1/miniapp/packs/{name}", { params: { path: { name } } })),
    onSuccess: (_, name) => {
      client.removeQueries({ queryKey: keys.pack(name) });
      return client.invalidateQueries({ queryKey: keys.packs });
    },
  });
}

export function useUnlinkDevice() {
  const api = useApi();
  const client = useQueryClient();
  return useMutation({
    mutationFn: (deviceId: string) =>
      call(api.DELETE("/v1/miniapp/devices/{deviceId}", { params: { path: { deviceId } } })),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.devices }),
  });
}
