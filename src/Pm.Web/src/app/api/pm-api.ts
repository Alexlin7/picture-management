import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface PhotoListItem { id: number; fileHash: string; width?: number; height?: number; mime?: string; }
export interface PhotoPage { items: PhotoListItem[]; nextCursor?: number | null; }
export interface TagView { id: number; name: string; kind: string; source: string; confidence?: number | null; }
export interface LocationView { libraryRootId: number; relPath: string; status: string; }
export interface PhotoDetail {
  id: number; fileHash: string; width?: number; height?: number; mime?: string;
  takenAt?: string | null; cameraModel?: string | null;
  locations: LocationView[]; tags: TagView[];
}
export interface Root { id: number; name: string; absPath: string; }
export interface PendingSegment { segment: string; count: number; samplePath: string; suggestedAction: string; }
export interface SearchReq { all?: string[]; none?: string[]; afterId?: number | null; pageSize?: number; }

@Injectable({ providedIn: 'root' })
export class PmApi {
  private http = inject(HttpClient);

  search(req: SearchReq): Promise<PhotoPage> {
    return firstValueFrom(this.http.post<PhotoPage>('/api/search', req));
  }
  photo(id: number): Promise<PhotoDetail> {
    return firstValueFrom(this.http.get<PhotoDetail>(`/api/photos/${id}`));
  }
  thumbUrl(id: number): string { return `/api/photos/${id}/thumb`; }

  roots(): Promise<Root[]> { return firstValueFrom(this.http.get<Root[]>('/api/roots')); }
  createRoot(name: string, absPath: string): Promise<Root> {
    return firstValueFrom(this.http.post<Root>('/api/roots', { name, absPath }));
  }
  scan(id: number): Promise<unknown> {
    return firstValueFrom(this.http.post(`/api/roots/${id}/scan`, {}));
  }
  missing(): Promise<{ id: number; fileHash: string; paths: string[] }[]> {
    return firstValueFrom(this.http.get<{ id: number; fileHash: string; paths: string[] }[]>('/api/reconcile/missing'));
  }
  pendingSegments(rootId: number): Promise<PendingSegment[]> {
    return firstValueFrom(this.http.get<PendingSegment[]>(`/api/roots/${rootId}/pending-segments`));
  }
  applyRule(dto: { rootId?: number; segment: string; action: string; tagName?: string }): Promise<unknown> {
    return firstValueFrom(this.http.post('/api/path-rules', dto));
  }
}
