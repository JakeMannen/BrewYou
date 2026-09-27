import { describe, it, expect, beforeEach, vi } from 'vitest';
import { i18n } from '$lib/i18n/index.svelte';
import type { BrewerySetup } from '$lib/types/brewery';
import type { BreweryRole, BreweryMemberDto } from '$lib/types/api';

describe('BreweryMembersModal Logic & Localization Contracts', () => {
	beforeEach(() => {
		vi.restoreAllMocks();
		i18n.init();
	});

	it('provides localized collaboration strings in both English and Swedish', () => {
		i18n.setLocale('en');
		expect(i18n.t('brewery.members_title')).toBe('Brewery Members');
		expect(i18n.t('brewery.invite_member')).toBe('Invite Member');
		expect(i18n.t('brewery.role_owner')).toBe('Co-Owner');
		expect(i18n.t('brewery.role_brewer')).toBe('Brewer');
		expect(i18n.t('brewery.role_viewer')).toBe('Viewer');
		expect(i18n.t('brewery.role_owner_desc')).toContain('Full administrative access');
		expect(i18n.t('brewery.role_brewer_desc')).toContain('Can brew batches');
		expect(i18n.t('brewery.role_viewer_desc')).toContain('read-only mode');
		expect(i18n.t('brewery.leave_brewery')).toBe('Leave Brewery');
		expect(i18n.t('brewery.shared_setup_badge')).toBe('Shared');
		expect(i18n.t('brewery.role_badge_primary_owner')).toBe('Primary Owner');

		i18n.setLocale('sv');
		expect(i18n.t('brewery.members_title')).toBe('Bryggerimedlemmar');
		expect(i18n.t('brewery.invite_member')).toBe('Bjud in medlem');
		expect(i18n.t('brewery.role_owner')).toBe('Medägare');
		expect(i18n.t('brewery.role_brewer')).toBe('Bryggare');
		expect(i18n.t('brewery.role_viewer')).toBe('Åskådare');
		expect(i18n.t('brewery.role_owner_desc')).toContain('Full administrativ behörighet');
		expect(i18n.t('brewery.role_brewer_desc')).toContain('Kan brygga batcher');
		expect(i18n.t('brewery.role_viewer_desc')).toContain('skrivskyddat läge');
		expect(i18n.t('brewery.leave_brewery')).toBe('Lämna bryggeri');
		expect(i18n.t('brewery.shared_setup_badge')).toBe('Delad');
		expect(i18n.t('brewery.role_badge_primary_owner')).toBe('Huvudägare');
	});

	it('derives owner permission correctly based on setup role', () => {
		const ownerSetup: BrewerySetup = {
			id: 'setup-1',
			name: 'Alpine Craft Rig',
			isDefault: true,
			equipmentCount: 3,
			isOwner: true,
			currentUserRole: 'Owner',
			memberCount: 2,
			createdAt: '2026-09-27T00:00:00Z'
		};

		const brewerSetup: BrewerySetup = {
			id: 'setup-2',
			name: 'Community Brewery',
			isDefault: false,
			equipmentCount: 5,
			isOwner: false,
			currentUserRole: 'Brewer',
			memberCount: 4,
			createdAt: '2026-09-27T00:00:00Z'
		};

		const viewerSetup: BrewerySetup = {
			id: 'setup-3',
			name: 'Partner Pilot System',
			isDefault: false,
			equipmentCount: 1,
			isOwner: false,
			currentUserRole: 'Viewer',
			memberCount: 3,
			createdAt: '2026-09-27T00:00:00Z'
		};

		const isOwnerFn = (s: BrewerySetup) => s.isOwner ?? s.currentUserRole === 'Owner';
		expect(isOwnerFn(ownerSetup)).toBe(true);
		expect(isOwnerFn(brewerSetup)).toBe(false);
		expect(isOwnerFn(viewerSetup)).toBe(false);
	});

	it('validates supported collaboration roles', () => {
		const validRoles: BreweryRole[] = ['Owner', 'Brewer', 'Viewer'];
		expect(validRoles).toContain('Owner');
		expect(validRoles).toContain('Brewer');
		expect(validRoles).toContain('Viewer');
		expect(validRoles.length).toBe(3);
	});

	it('formats ISO dates accurately for member joined date display', () => {
		const formatDate = (iso: string) => {
			try {
				const d = new Date(iso);
				return d.toLocaleDateString('en-US', {
					year: 'numeric',
					month: 'short',
					day: 'numeric'
				});
			} catch {
				return iso;
			}
		};

		const formatted = formatDate('2026-06-15T12:00:00Z');
		expect(formatted).toContain('2026');
		expect(formatted).toContain('Jun');
	});

	it('supports interpolating joined_on and delete confirmation with translation parameters', () => {
		i18n.setLocale('en');
		const joinedText = i18n.t('brewery.joined_on', { date: 'Jun 15, 2026' });
		expect(joinedText).toBe('Joined Jun 15, 2026');

		const confirmText = i18n.t('brewery.delete_warning', { name: 'My Old Rig' });
		expect(confirmText).toBe(
			'Are you sure you want to delete "My Old Rig"? This action cannot be undone.'
		);

		i18n.setLocale('sv');
		const joinedTextSv = i18n.t('brewery.joined_on', { date: '15 juni 2026' });
		expect(joinedTextSv).toBe('Gick med 15 juni 2026');
	});

	it('strictly forbids deleting or modifying the original owner or last remaining owner', () => {
		const isMemberLastOrPrimaryOwner = (
			member: BreweryMemberDto,
			memberList: BreweryMemberDto[]
		) => {
			if (member.isPrimaryOwner) return true;
			const totalOwners = memberList.filter((m) => m.role === 'Owner').length;
			return member.role === 'Owner' && totalOwners <= 1;
		};

		const canModifyMember = (
			member: BreweryMemberDto,
			memberList: BreweryMemberDto[],
			isOwner: boolean,
			currentUserId?: string
		) => {
			if (!isOwner) return false;
			if (isMemberLastOrPrimaryOwner(member, memberList)) return false;
			if (currentUserId && currentUserId === member.userId) return false;
			return true;
		};

		const originalOwner: BreweryMemberDto = {
			id: 'm-1',
			userId: 'owner-user-1',
			email: 'founder@brewyou.test',
			displayName: 'Brewery Founder',
			role: 'Owner',
			joinedAt: '2026-01-01T00:00:00Z',
			isPrimaryOwner: true
		};

		const coOwner: BreweryMemberDto = {
			id: 'm-2',
			userId: 'co-owner-user',
			email: 'partner@brewyou.test',
			displayName: 'Partner Brewer',
			role: 'Owner',
			joinedAt: '2026-02-01T00:00:00Z',
			isPrimaryOwner: false
		};

		const guestBrewer: BreweryMemberDto = {
			id: 'm-3',
			userId: 'guest-user',
			email: 'guest@brewyou.test',
			displayName: 'Guest Brewer',
			role: 'Brewer',
			joinedAt: '2026-03-01T00:00:00Z',
			isPrimaryOwner: false
		};

		const allMembers = [originalOwner, coOwner, guestBrewer];

		// 1. Original owner must never have a delete button, even when inspected by an owner
		expect(canModifyMember(originalOwner, allMembers, true, 'owner-user-1')).toBe(false);
		expect(canModifyMember(originalOwner, allMembers, true, 'co-owner-user')).toBe(false);

		// 2. Co-owner can be modified when multiple owners exist
		expect(canModifyMember(coOwner, allMembers, true, 'owner-user-1')).toBe(true);

		// 3. But if co-owner is the only remaining owner in a setup, they cannot be deleted/modified
		const soleOwnerList = [coOwner, guestBrewer];
		expect(canModifyMember(coOwner, soleOwnerList, true, 'other-owner')).toBe(false);

		// 4. Normal collaborator (Brewer) can be modified by the owner
		expect(canModifyMember(guestBrewer, allMembers, true, 'owner-user-1')).toBe(true);

		// 5. User cannot delete themselves via the member action button
		expect(canModifyMember(guestBrewer, allMembers, true, 'guest-user')).toBe(false);
	});
});
