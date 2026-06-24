import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
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
export interface SavedSearchRow { id: number; name: string; queryJson: string; createdAt: string; }
export interface TagTreeNode { name: string; kind: string; count: number; multi?: boolean; children?: TagTreeNode[] | null; }
export interface TagTree {
  tree: TagTreeNode[]; rootless: TagTreeNode[];
  general: [string, number][]; meta: [string, number][];
}
export interface TagListRow { id: number; name: string; kind: string; count: number; }

@Injectable({ providedIn: 'root' })
export class PmApi {
  private http = inject(HttpClient);

  search(req: SearchReq): Promise<PhotoPage> {
    return firstValueFrom(this.http.post<PhotoPage>('/api/search', req));
  }
  searchCount(req: SearchReq): Promise<{ total: number }> {
    return firstValueFrom(this.http.post<{ total: number }>('/api/search/count', req));
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
  applyPathTags(rootId: number): Promise<unknown> {
    return firstValueFrom(this.http.post(`/api/roots/${rootId}/apply-path-tags`, {}));
  }

  savedSearches(): Promise<SavedSearchRow[]> {
    return firstValueFrom(this.http.get<SavedSearchRow[]>('/api/saved-searches'));
  }
  createSavedSearch(dto: { name: string; queryJson: string }): Promise<{ id: number }> {
    return firstValueFrom(this.http.post<{ id: number }>('/api/saved-searches', dto));
  }
  deleteSavedSearch(id: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/saved-searches/${id}`));
  }

  tagTree(): Promise<TagTree> {
    return firstValueFrom(this.http.get<TagTree>('/api/tags/tree'));
  }

  archivePhoto(id: number): Promise<{ archived: number }> {
    return firstValueFrom(this.http.post<{ archived: number }>(`/api/photos/${id}/archive`, {}));
  }
  purgePhoto(id: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/photos/${id}`));
  }
  addTag(photoId: number, dto: { name: string; kind?: string }): Promise<TagView> {
    return firstValueFrom(this.http.post<TagView>(`/api/photos/${photoId}/tags`, dto));
  }
  removeTag(photoId: number, tagId: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/photos/${photoId}/tags/${tagId}`));
  }

  // 單張重標 / 清除 WD14 自動標。mode:refresh(清舊 wd14 + 重排)/ clear(清舊 wd14 不排)
  // / retry(重排失敗)。後端動作層,不碰檔案系統。
  retag(photoId: number, mode: 'retry' | 'refresh' | 'clear'): Promise<unknown> {
    return firstValueFrom(
      this.http.post(`/api/photos/${photoId}/retag`, null, { params: new HttpParams().set('mode', mode) }),
    );
  }

  taggingStats(): Promise<{ pending: number; error: number; running: number }> {
    return firstValueFrom(
      this.http.get<{ pending: number; error: number; running: number }>('/api/tagging/stats'),
    );
  }

  // 批次 requeue:維護動作(非破壞)。mode:retry=重排失敗 / refresh=清 wd14 重排 / clear=清不排。
  // scope 四選一:photoIds/error/root/all。回傳 matched/clearedTags/jobsCreated/jobsUpdated。
  requeue(
    mode: 'retry' | 'refresh' | 'clear',
    scope: { photoIds?: number[]; error?: boolean; root?: number; all?: boolean },
  ): Promise<{ matched: number; clearedTags: number; jobsCreated: number; jobsUpdated: number }> {
    return firstValueFrom(
      this.http.post<{ matched: number; clearedTags: number; jobsCreated: number; jobsUpdated: number }>(
        '/api/tag/requeue',
        { mode, scope },
      ),
    );
  }

  // ---- 標籤庫(管理頁 + autocomplete 共用)----
  tags(q?: string, limit?: number): Promise<TagListRow[]> {
    let params = new HttpParams();
    if (q) params = params.set('q', q);
    if (limit != null) params = params.set('limit', String(limit));
    return firstValueFrom(this.http.get<TagListRow[]>('/api/tags', { params }));
  }
  createTag(name: string, kind?: string): Promise<{ id: number; name: string; kind: string; existed: boolean }> {
    return firstValueFrom(
      this.http.post<{ id: number; name: string; kind: string; existed: boolean }>('/api/tags', { name, kind }),
    );
  }
  updateTag(id: number, dto: { name?: string; kind?: string }): Promise<{ merged: boolean }> {
    return firstValueFrom(this.http.put<{ merged: boolean }>(`/api/tags/${id}`, dto));
  }
  deleteTag(id: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/tags/${id}`));
  }
  mergeTags(id: number, targetId: number): Promise<unknown> {
    return firstValueFrom(this.http.post(`/api/tags/${id}/merge/${targetId}`, {}));
  }
}
