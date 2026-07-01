import { defineStore } from 'pinia';

export type WorkspaceShellMode = 'flow' | 'tool' | 'review';

export const MIN_DOCK_WIDTH = 180;
export const MAX_DOCK_WIDTH = 360;
export const DEFAULT_LEFT_DOCK_WIDTH = 232;
export const DEFAULT_RIGHT_DOCK_WIDTH = 284;

interface WorkspaceShellUiState {
  activeMode: WorkspaceShellMode;
  leftDockCollapsed: boolean;
  rightDockCollapsed: boolean;
  leftDockWidth: number;
  rightDockWidth: number;
}

function clampDockWidth(width: number): number {
  if (!Number.isFinite(width)) {
    return DEFAULT_LEFT_DOCK_WIDTH;
  }

  return Math.min(MAX_DOCK_WIDTH, Math.max(MIN_DOCK_WIDTH, Math.round(width)));
}

export const useWorkspaceShellStore = defineStore('studio2.workspaceShell.ui', {
  state: (): WorkspaceShellUiState => ({
    activeMode: 'flow',
    leftDockCollapsed: false,
    rightDockCollapsed: false,
    leftDockWidth: DEFAULT_LEFT_DOCK_WIDTH,
    rightDockWidth: DEFAULT_RIGHT_DOCK_WIDTH
  }),
  actions: {
    setMode(mode: WorkspaceShellMode): void {
      this.activeMode = mode;
    },
    toggleLeftDock(): void {
      this.leftDockCollapsed = !this.leftDockCollapsed;
    },
    toggleRightDock(): void {
      this.rightDockCollapsed = !this.rightDockCollapsed;
    },
    setLeftDockWidth(width: number): void {
      this.leftDockWidth = clampDockWidth(width);
    },
    setRightDockWidth(width: number): void {
      this.rightDockWidth = clampDockWidth(width);
    }
  }
});
