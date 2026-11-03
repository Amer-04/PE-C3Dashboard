import { Routes } from '@angular/router';
import { DashboardPage } from './features/dashboard/dashboard.page';

export const routes: Routes = [
  { path: '', component: DashboardPage, title: 'C3 Site Finder · EV Edison' },
  {
    path: 'saved',
    loadComponent: () => import('./features/saved/saved.page').then(m => m.SavedPage),
    title: 'Saved sites · C3 Site Finder',
  },
  {
    path: 'saved/:id',
    loadComponent: () => import('./features/saved/saved-workspace.page').then(m => m.SavedWorkspacePage),
    title: 'Work saved site · C3 Site Finder',
  },
  {
    path: 'screening',
    loadComponent: () => import('./features/screening/screening.page').then(m => m.ScreeningPage),
    title: 'Site screening · C3 Site Finder',
  },
  {
    path: 'compare',
    loadComponent: () => import('./features/compare/compare.page').then(m => m.ComparePage),
    title: 'Compare sites · C3 Site Finder',
  },
  {
    path: 'methodology',
    loadComponent: () => import('./features/methodology/methodology.page').then(m => m.MethodologyPage),
    title: 'Methodology · C3 Site Finder',
  },
  { path: '**', redirectTo: '' },
];
