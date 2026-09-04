// Companion of PasskeySubmit.razor (standard .NET 10 Identity template pattern, German messages). Runs the
// WebAuthn ceremony in the browser and posts the credential back through the surrounding <form>.

const browserSupportsPasskeys =
    typeof navigator.credentials !== 'undefined' &&
    typeof window.PublicKeyCredential !== 'undefined' &&
    typeof window.PublicKeyCredential.parseCreationOptionsFromJSON === 'function' &&
    typeof window.PublicKeyCredential.parseRequestOptionsFromJSON === 'function';

async function fetchWithErrorHandling(url, options = {}) {
    const response = await fetch(url, { credentials: 'include', ...options });
    if (!response.ok) {
        const text = await response.text();
        console.error(text);
        throw new Error(`Der Server hat mit Status ${response.status} geantwortet.`);
    }
    return response;
}

async function createCredential(headers, signal) {
    const optionsResponse = await fetchWithErrorHandling('/Account/PasskeyCreationOptions', { method: 'POST', headers, signal });
    const options = PublicKeyCredential.parseCreationOptionsFromJSON(await optionsResponse.json());
    return await navigator.credentials.create({ publicKey: options, signal });
}

async function requestCredential(email, mediation, headers, signal) {
    const optionsResponse = await fetchWithErrorHandling(`/Account/PasskeyRequestOptions?username=${encodeURIComponent(email ?? '')}`, { method: 'POST', headers, signal });
    const options = PublicKeyCredential.parseRequestOptionsFromJSON(await optionsResponse.json());
    return await navigator.credentials.get({ publicKey: options, mediation, signal });
}

customElements.define('passkey-submit', class extends HTMLElement {
    static formAssociated = true;

    connectedCallback() {
        this.internals = this.attachInternals();
        this.attrs = {
            operation: this.getAttribute('operation'),
            name: this.getAttribute('name'),
            emailName: this.getAttribute('email-name'),
            requestTokenName: this.getAttribute('request-token-name'),
            requestTokenValue: this.getAttribute('request-token-value'),
        };

        this.internals.form.addEventListener('submit', (event) => {
            if (event.submitter?.name === '__passkeySubmit') {
                event.preventDefault();
                this.obtainAndSubmitCredential();
            }
        });

        this.tryAutofillPasskey();
    }

    disconnectedCallback() {
        this.abortController?.abort();
    }

    async obtainCredential(useConditionalMediation, signal) {
        if (!browserSupportsPasskeys) {
            throw new Error('Dieser Browser unterstützt Passkeys nicht vollständig – bitte aktualisieren.');
        }

        const headers = { [this.attrs.requestTokenName]: this.attrs.requestTokenValue };

        if (this.attrs.operation === 'Create') {
            return await createCredential(headers, signal);
        } else if (this.attrs.operation === 'Request') {
            const email = new FormData(this.internals.form).get(this.attrs.emailName);
            const mediation = useConditionalMediation ? 'conditional' : undefined;
            return await requestCredential(email, mediation, headers, signal);
        } else {
            throw new Error(`Unbekannte Passkey-Operation '${this.attrs.operation}'.`);
        }
    }

    async obtainAndSubmitCredential(useConditionalMediation = false) {
        this.abortController?.abort();
        this.abortController = new AbortController();
        const signal = this.abortController.signal;
        const formData = new FormData();
        try {
            const credential = await this.obtainCredential(useConditionalMediation, signal);
            formData.append(`${this.attrs.name}.CredentialJson`, JSON.stringify(credential));
        } catch (error) {
            if (error.name === 'AbortError') {
                return; // the learner cancelled - nothing to report
            }
            console.error(error);
            if (useConditionalMediation) {
                return; // autofill attempt, not user-initiated - stay quiet
            }
            const errorMessage = error.name === 'NotAllowedError'
                ? 'Der Authenticator hat keinen Passkey geliefert.'
                : error.message;
            formData.append(`${this.attrs.name}.Error`, errorMessage);
        }
        this.internals.setFormValue(formData);
        this.internals.form.submit();
    }

    async tryAutofillPasskey() {
        if (browserSupportsPasskeys && this.attrs.operation === 'Request' && await PublicKeyCredential.isConditionalMediationAvailable?.()) {
            await this.obtainAndSubmitCredential(/* useConditionalMediation */ true);
        }
    }
});
