import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import {
  Api,
  SellerMemberDto,
  apiV1SellersMeTeamGet,
  apiV1SellersMeTeamMemberIdDelete,
  apiV1SellersMeTeamPost,
} from '@upbazaar/data-access';
import { provideI18n } from '@upbazaar/ui';
import { TeamPage } from './team.page';

const ravi: SellerMemberDto = {
  id: 'm1',
  userId: 'u2',
  displayName: 'Ravi Packer',
  email: 'ravi@example.com',
  role: 'Dispatch',
  addedAtUtc: '2026-09-28T10:00:00Z',
};

async function render() {
  const invoke = vi.fn(async (fn: unknown) => (fn === apiV1SellersMeTeamGet ? [ravi] : ravi));

  TestBed.configureTestingModule({
    providers: [provideZonelessChangeDetection(), provideI18n(), { provide: Api, useValue: { invoke } }],
  });

  const fixture = TestBed.createComponent(TeamPage);
  await fixture.whenStable();

  return { fixture, invoke, element: fixture.nativeElement as HTMLElement };
}

describe('TeamPage', () => {
  it('lists the team with what each role may do', async () => {
    const { element } = await render();

    expect(element.textContent).toContain('Ravi Packer');
    expect(element.textContent).toContain('Orders, packing and returns only.');
  });

  it('adds someone by their account email', async () => {
    const { fixture, invoke, element } = await render();

    const email = element.querySelector('input[name="email"]') as HTMLInputElement;
    email.value = ' sita@example.com ';
    email.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1SellersMeTeamPost, { body: { email: 'sita@example.com', role: 'Dispatch' } });
  });

  it('takes someone off the team', async () => {
    const { fixture, invoke, element } = await render();

    [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Remove')!.click();
    await fixture.whenStable();

    expect(invoke).toHaveBeenCalledWith(apiV1SellersMeTeamMemberIdDelete, { memberId: 'm1' });
  });
});
